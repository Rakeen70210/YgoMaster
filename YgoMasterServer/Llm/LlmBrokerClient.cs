using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace YgoMaster
{
    interface ILlmBrokerTransport
    {
        string PostDecisionRequest(string requestJson, int timeoutMs);
    }

    class LlmBrokerTransportException : Exception
    {
        public string ResponseJson { get; private set; }

        public LlmBrokerTransportException(string message, string responseJson)
            : base(message)
        {
            ResponseJson = responseJson;
        }

        public LlmBrokerTransportException(string message, string responseJson, Exception innerException)
            : base(message, innerException)
        {
            ResponseJson = responseJson;
        }
    }

    class LlmBrokerDecisionResult
    {
        public bool IsSuccess { get; private set; }
        public string Error { get; private set; }
        public string ErrorDetail { get; private set; }
        public string RequestJson { get; private set; }
        public string ResponseJson { get; private set; }
        public long? LatencyMs { get; private set; }
        public LlmBrokerDecisionResponse Response { get; private set; }
        public LegalAction Action { get; private set; }

        public static LlmBrokerDecisionResult Success(
            string requestJson,
            string responseJson,
            LlmBrokerDecisionResponse response,
            LegalAction action,
            long? latencyMs)
        {
            return new LlmBrokerDecisionResult()
            {
                IsSuccess = true,
                RequestJson = requestJson,
                ResponseJson = responseJson,
                LatencyMs = latencyMs,
                Response = response,
                Action = action,
            };
        }

        public static LlmBrokerDecisionResult Success(
            string requestJson,
            string responseJson,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            return Success(requestJson, responseJson, response, action, null);
        }

        public static LlmBrokerDecisionResult Failure(
            string error,
            string requestJson,
            string responseJson,
            string errorDetail,
            long? latencyMs)
        {
            return Failure(error, requestJson, responseJson, errorDetail, latencyMs, null, null);
        }

        public static LlmBrokerDecisionResult Failure(
            string error,
            string requestJson,
            string responseJson,
            string errorDetail,
            long? latencyMs,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            return new LlmBrokerDecisionResult()
            {
                IsSuccess = false,
                Error = error,
                ErrorDetail = errorDetail,
                RequestJson = requestJson,
                ResponseJson = responseJson,
                LatencyMs = latencyMs,
                Response = response,
                Action = action,
            };
        }

        public static LlmBrokerDecisionResult Failure(
            string error,
            string requestJson,
            string responseJson,
            string errorDetail)
        {
            return Failure(error, requestJson, responseJson, errorDetail, null);
        }

        public static LlmBrokerDecisionResult Failure(
            string error,
            string requestJson,
            string responseJson)
        {
            return Failure(error, requestJson, responseJson, null);
        }
    }

    static class LlmBrokerClient
    {
        public static LlmBrokerDecisionResult RequestDecision(
            DecisionSnapshot snapshot,
            ILlmBrokerTransport transport,
            int timeoutMs)
        {
            if (snapshot == null)
            {
                return LlmBrokerDecisionResult.Failure("missing_snapshot", null, null);
            }
            if (transport == null)
            {
                return LlmBrokerDecisionResult.Failure("missing_transport", null, null);
            }

            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            string responseJson = null;
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                responseJson = transport.PostDecisionRequest(requestJson, timeoutMs);
            }
            catch (LlmBrokerTransportException e)
            {
                stopwatch.Stop();
                responseJson = e.ResponseJson;
                string transportError;
                string transportErrorDetail;
                if (!LlmBrokerProtocol.TryParseErrorResponse(
                    responseJson, out transportError, out transportErrorDetail))
                {
                    transportError = "transport_error";
                    transportErrorDetail = null;
                }
                return LlmBrokerDecisionResult.Failure(
                    transportError,
                    requestJson,
                    responseJson,
                    transportErrorDetail,
                    stopwatch.ElapsedMilliseconds);
            }
            catch
            {
                stopwatch.Stop();
                return LlmBrokerDecisionResult.Failure(
                    "transport_error", requestJson, responseJson, null, stopwatch.ElapsedMilliseconds);
            }
            stopwatch.Stop();

            LlmBrokerDecisionResponse response;
            string error;
            if (!LlmBrokerProtocol.TryParseDecisionResponse(responseJson, out response, out error))
            {
                return LlmBrokerDecisionResult.Failure(
                    error, requestJson, responseJson, null, stopwatch.ElapsedMilliseconds);
            }

            LlmBrokerValidationResult validation = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                response,
                null,
                stopwatch.ElapsedMilliseconds);
            if (!validation.IsValid)
            {
                // Keep parsed response + matched action on quality soft-fails so the client
                // can recover without re-walking response JSON.
                return LlmBrokerDecisionResult.Failure(
                    validation.Error,
                    requestJson,
                    responseJson,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    response,
                    validation.Action);
            }

            return LlmBrokerDecisionResult.Success(
                requestJson,
                responseJson,
                response,
                validation.Action,
                stopwatch.ElapsedMilliseconds);
        }
    }

    class HttpLlmBrokerTransport : ILlmBrokerTransport
    {
        readonly string url;

        public HttpLlmBrokerTransport(string url)
        {
            this.url = url;
        }

        public string PostDecisionRequest(string requestJson, int timeoutMs)
        {
            byte[] data = Encoding.UTF8.GetBytes(requestJson);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.ContentLength = data.Length;

            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException e)
            {
                throw new LlmBrokerTransportException(
                    e.Message,
                    ReadResponseBody(e.Response),
                    e);
            }
        }

        static string ReadResponseBody(WebResponse response)
        {
            if (response == null)
            {
                return null;
            }

            using (response)
            using (Stream responseStream = response.GetResponseStream())
            {
                if (responseStream == null)
                {
                    return null;
                }
                using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
