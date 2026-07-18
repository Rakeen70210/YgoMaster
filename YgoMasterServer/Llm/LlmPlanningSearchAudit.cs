using System;
using System.Collections.Generic;
using System.Reflection;

namespace YgoMaster
{
    /// <summary>
    /// Fail-closed audit wrapper for Layer A graph construction (YGOMASTER-LLM-005 Slice 2A).
    /// Never mutates the one-step decision request; swallows factory/builder exceptions.
    /// MaxSerializedBytes below MinimumSupported fails closed with explicit error and no Projection.
    /// </summary>
    static class LlmPlanningSearchAudit
    {
        public static LlmPlanningSearchAuditResult TryBuild(
            DecisionSnapshot snapshot,
            LlmSelfResources selfResources,
            LlmSearchLimits limits)
        {
            return TryBuild(snapshot, selfResources, limits, (object)null);
        }

        public static LlmPlanningSearchAuditResult TryBuild(
            DecisionSnapshot snapshot,
            LlmSelfResources selfResources,
            LlmSearchLimits limits,
            Func<DecisionSnapshot, LlmSelfResources, LlmSearchLimits, object> graphFactory)
        {
            return TryBuild(snapshot, selfResources, limits, (object)graphFactory);
        }

        public static LlmPlanningSearchAuditResult TryBuild(
            DecisionSnapshot snapshot,
            LlmSelfResources selfResources,
            LlmSearchLimits limits,
            object graphFactory)
        {
            if (limits == null)
            {
                limits = LlmSearchLimits.CreateDefault();
            }
            else
            {
                // Preserve MaxSerializedBytes for strict floor validation (do not clamp it up).
                ILlmSearchBudgetClock clockPreserve = limits.Clock;
                int rawSerialized = limits.MaxSerializedBytes;
                limits = limits.Clone();
                limits.Clock = clockPreserve;
                limits.MaxSerializedBytes = rawSerialized;
            }

            ILlmSearchBudgetClock clock = null;
            if (limits.Clock != null)
            {
                clock = limits.Clock;
            }
            else
            {
                clock = new LlmSearchSystemBudgetClock();
                limits.Clock = clock;
            }

            // Strict serialized-budget contract before any projection work.
            int checkBytes = limits.MaxSerializedBytes > 0
                ? limits.MaxSerializedBytes
                : LlmSearchLimits.DefaultMaxSerializedBytes;
            string budgetError = LlmSearchLimits.ValidateSerializedBudgetOrError(checkBytes);
            if (budgetError != null)
            {
                return new LlmPlanningSearchAuditResult()
                {
                    Success = false,
                    Error = budgetError,
                    Graph = null,
                    Projection = null,
                    ElapsedMs = clock.ElapsedMilliseconds,
                    Status = "failed",
                };
            }
            if (limits.MaxSerializedBytes <= 0)
            {
                limits.MaxSerializedBytes = LlmSearchLimits.DefaultMaxSerializedBytes;
            }

            try
            {
                object graphObj = InvokeFactory(snapshot, selfResources, limits, graphFactory);
                LlmSearchGraph typed = graphObj as LlmSearchGraph;
                if (typed == null && graphObj != null)
                {
                    return Failure("graph_factory_returned_unexpected_type", clock);
                }
                Dictionary<string, object> projection = null;
                if (typed != null)
                {
                    projection = LlmSearchProjection.Project(typed);
                    // Enforce hard UTF-8 budget for every accepted max.
                    string json = MiniJSON.Json.Serialize(projection);
                    int utf8 = System.Text.Encoding.UTF8.GetByteCount(json);
                    if (utf8 > limits.MaxSerializedBytes)
                    {
                        return new LlmPlanningSearchAuditResult()
                        {
                            Success = false,
                            Error = "max_serialized_bytes_exceeded:" + utf8 + ">" + limits.MaxSerializedBytes,
                            Graph = typed,
                            Projection = null,
                            ElapsedMs = clock.ElapsedMilliseconds,
                            Status = "failed",
                        };
                    }
                }
                // Fail closed if projection reports budget_failed (roots cannot fit).
                if (projection != null
                    && string.Equals(
                        Convert.ToString(projection.ContainsKey("status") ? projection["status"] : null),
                        "budget_failed",
                        StringComparison.Ordinal))
                {
                    return new LlmPlanningSearchAuditResult()
                    {
                        Success = false,
                        Error = Convert.ToString(
                            projection.ContainsKey("error") ? projection["error"] : "budget_failed"),
                        Graph = typed,
                        Projection = projection,
                        ElapsedMs = clock.ElapsedMilliseconds,
                        Status = "budget_failed",
                    };
                }
                return new LlmPlanningSearchAuditResult()
                {
                    Success = true,
                    Error = null,
                    Graph = typed,
                    Projection = projection,
                    ElapsedMs = clock.ElapsedMilliseconds,
                    Status = typed != null ? typed.Status : "ok",
                };
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner is TargetInvocationException && inner.InnerException != null)
                {
                    inner = inner.InnerException;
                }
                // ArgumentOutOfRange on MaxSerializedBytes → explicit minimum error, no Projection.
                if (inner is ArgumentOutOfRangeException)
                {
                    string msg = inner.Message ?? string.Empty;
                    if (msg.IndexOf("max_serialized_bytes_below_minimum", StringComparison.Ordinal) >= 0
                        || msg.IndexOf("MinimumSupported", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return new LlmPlanningSearchAuditResult()
                        {
                            Success = false,
                            Error = msg.Contains("max_serialized_bytes_below_minimum")
                                ? ExtractMinimumError(msg)
                                : ("max_serialized_bytes_below_minimum:"
                                    + limits.MaxSerializedBytes + "<"
                                    + LlmSearchProjection.MinimumSupportedSerializedBytes),
                            Graph = null,
                            Projection = null,
                            ElapsedMs = clock.ElapsedMilliseconds,
                            Status = "failed",
                        };
                    }
                }
                return Failure(inner != null ? inner.Message : ex.Message, clock);
            }
        }

        static string ExtractMinimumError(string message)
        {
            int idx = message.IndexOf("max_serialized_bytes_below_minimum", StringComparison.Ordinal);
            if (idx < 0)
            {
                return message;
            }
            // Take through first sentence terminator / newline if present.
            int end = message.IndexOfAny(new[] { '\r', '\n', '.' }, idx);
            if (end < 0)
            {
                return message.Substring(idx).Trim();
            }
            return message.Substring(idx, end - idx).Trim();
        }

        static LlmPlanningSearchAuditResult Failure(string error, ILlmSearchBudgetClock clock)
        {
            return new LlmPlanningSearchAuditResult()
            {
                Success = false,
                Error = error,
                Graph = null,
                Projection = null,
                ElapsedMs = clock != null ? clock.ElapsedMilliseconds : 0,
                Status = "failed",
            };
        }

        static object InvokeFactory(
            DecisionSnapshot snapshot,
            LlmSelfResources selfResources,
            LlmSearchLimits limits,
            object graphFactory)
        {
            if (graphFactory == null)
            {
                // Slice 6B: default production path augments Layer A with combo templates.
                return LlmComboTemplateMatcher.ExpandToGraph(snapshot, selfResources, limits);
            }

            Func<DecisionSnapshot, LlmSelfResources, LlmSearchLimits, object> func3 =
                graphFactory as Func<DecisionSnapshot, LlmSelfResources, LlmSearchLimits, object>;
            if (func3 != null)
            {
                return func3(snapshot, selfResources, limits);
            }

            Func<object> func0 = graphFactory as Func<object>;
            if (func0 != null)
            {
                return func0();
            }

            ILlmSearchGraphFactory typedFactory = graphFactory as ILlmSearchGraphFactory;
            if (typedFactory != null)
            {
                return typedFactory.Build(snapshot, selfResources, limits);
            }

            Type factoryType = graphFactory.GetType();
            MethodInfo[] methods = factoryType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (MethodInfo method in methods)
            {
                if (method.Name != "Build")
                {
                    continue;
                }
                ParameterInfo[] ps = method.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    Type pt = ps[i].ParameterType;
                    if (pt.IsInstanceOfType(snapshot) || pt == typeof(DecisionSnapshot))
                    {
                        args[i] = snapshot;
                    }
                    else if (selfResources != null && pt.IsInstanceOfType(selfResources))
                    {
                        args[i] = selfResources;
                    }
                    else if (limits != null && pt.IsInstanceOfType(limits))
                    {
                        args[i] = limits;
                    }
                    else if (pt == typeof(object))
                    {
                        if (i == 0)
                        {
                            args[i] = snapshot;
                        }
                        else if (i == 1)
                        {
                            args[i] = selfResources;
                        }
                        else if (i == 2)
                        {
                            args[i] = limits;
                        }
                        else
                        {
                            args[i] = null;
                        }
                    }
                    else
                    {
                        args[i] = null;
                    }
                }
                return method.Invoke(graphFactory, args);
            }

            throw new InvalidOperationException(
                "graph_factory_has_no_usable_Build_method: " + factoryType.FullName);
        }
    }
}
