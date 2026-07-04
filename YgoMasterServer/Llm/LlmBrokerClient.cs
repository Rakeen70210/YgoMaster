using System;
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
        public LlmBrokerDecisionResponse Response { get; private set; }
        public LegalAction Action { get; private set; }

        public static LlmBrokerDecisionResult Success(
            string requestJson,
            string responseJson,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            return new LlmBrokerDecisionResult()
            {
                IsSuccess = true,
                RequestJson = requestJson,
                ResponseJson = responseJson,
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
            return new LlmBrokerDecisionResult()
            {
                IsSuccess = false,
                Error = error,
                ErrorDetail = errorDetail,
                RequestJson = requestJson,
                ResponseJson = responseJson,
            };
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
            try
            {
                responseJson = transport.PostDecisionRequest(requestJson, timeoutMs);
            }
            catch (LlmBrokerTransportException e)
            {
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
                    transportError, requestJson, responseJson, transportErrorDetail);
            }
            catch
            {
                return LlmBrokerDecisionResult.Failure("transport_error", requestJson, responseJson);
            }

            LlmBrokerDecisionResponse response;
            string error;
            if (!LlmBrokerProtocol.TryParseDecisionResponse(responseJson, out response, out error))
            {
                return LlmBrokerDecisionResult.Failure(error, requestJson, responseJson);
            }

            LlmBrokerValidationResult validation = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            if (!validation.IsValid)
            {
                return LlmBrokerDecisionResult.Failure(validation.Error, requestJson, responseJson);
            }

            return LlmBrokerDecisionResult.Success(requestJson, responseJson, response, validation.Action);
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
