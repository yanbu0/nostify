using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests
{
    /// <summary>
    /// Performance comparison tests for NostifyObject.UpdateProperties implementations.
    /// This does NOT change the production implementation, it just re-implements
    /// the current and optimized logic side-by-side and compares execution time
    /// under synthetic load.
    /// </summary>
    public class UpdatePropertiesPerfTests
    {
        #region Test Aggregate and Payload

        /// <summary>
        /// Large aggregate type with many writable properties to amplify
        /// reflection and JObject costs in the benchmark.
        /// </summary>
        private class LargeAggregate : NostifyObject
        {
            // 100 string properties
            public string P01 { get; set; }
            public string P02 { get; set; }
            public string P03 { get; set; }
            public string P04 { get; set; }
            public string P05 { get; set; }
            public string P06 { get; set; }
            public string P07 { get; set; }
            public string P08 { get; set; }
            public string P09 { get; set; }
            public string P10 { get; set; }
            public string P11 { get; set; }
            public string P12 { get; set; }
            public string P13 { get; set; }
            public string P14 { get; set; }
            public string P15 { get; set; }
            public string P16 { get; set; }
            public string P17 { get; set; }
            public string P18 { get; set; }
            public string P19 { get; set; }
            public string P20 { get; set; }
            public string P21 { get; set; }
            public string P22 { get; set; }
            public string P23 { get; set; }
            public string P24 { get; set; }
            public string P25 { get; set; }
            public string P26 { get; set; }
            public string P27 { get; set; }
            public string P28 { get; set; }
            public string P29 { get; set; }
            public string P30 { get; set; }
            public string P31 { get; set; }
            public string P32 { get; set; }
            public string P33 { get; set; }
            public string P34 { get; set; }
            public string P35 { get; set; }
            public string P36 { get; set; }
            public string P37 { get; set; }
            public string P38 { get; set; }
            public string P39 { get; set; }
            public string P40 { get; set; }
            public string P41 { get; set; }
            public string P42 { get; set; }
            public string P43 { get; set; }
            public string P44 { get; set; }
            public string P45 { get; set; }
            public string P46 { get; set; }
            public string P47 { get; set; }
            public string P48 { get; set; }
            public string P49 { get; set; }
            public string P50 { get; set; }
            public string P51 { get; set; }
            public string P52 { get; set; }
            public string P53 { get; set; }
            public string P54 { get; set; }
            public string P55 { get; set; }
            public string P56 { get; set; }
            public string P57 { get; set; }
            public string P58 { get; set; }
            public string P59 { get; set; }
            public string P60 { get; set; }
            public string P61 { get; set; }
            public string P62 { get; set; }
            public string P63 { get; set; }
            public string P64 { get; set; }
            public string P65 { get; set; }
            public string P66 { get; set; }
            public string P67 { get; set; }
            public string P68 { get; set; }
            public string P69 { get; set; }
            public string P70 { get; set; }
            public string P71 { get; set; }
            public string P72 { get; set; }
            public string P73 { get; set; }
            public string P74 { get; set; }
            public string P75 { get; set; }
            public string P76 { get; set; }
            public string P77 { get; set; }
            public string P78 { get; set; }
            public string P79 { get; set; }
            public string P80 { get; set; }
            public string P81 { get; set; }
            public string P82 { get; set; }
            public string P83 { get; set; }
            public string P84 { get; set; }
            public string P85 { get; set; }
            public string P86 { get; set; }
            public string P87 { get; set; }
            public string P88 { get; set; }
            public string P89 { get; set; }
            public string P90 { get; set; }
            public string P91 { get; set; }
            public string P92 { get; set; }
            public string P93 { get; set; }
            public string P94 { get; set; }
            public string P95 { get; set; }
            public string P96 { get; set; }
            public string P97 { get; set; }
            public string P98 { get; set; }
            public string P99 { get; set; }
            public string P100 { get; set; }

            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                // not needed for perf harness
                throw new NotImplementedException();
            }
        }

        private class LargePayload
        {
            // 100 string properties matching LargeAggregate
            public string P01 { get; set; }
            public string P02 { get; set; }
            public string P03 { get; set; }
            public string P04 { get; set; }
            public string P05 { get; set; }
            public string P06 { get; set; }
            public string P07 { get; set; }
            public string P08 { get; set; }
            public string P09 { get; set; }
            public string P10 { get; set; }
            public string P11 { get; set; }
            public string P12 { get; set; }
            public string P13 { get; set; }
            public string P14 { get; set; }
            public string P15 { get; set; }
            public string P16 { get; set; }
            public string P17 { get; set; }
            public string P18 { get; set; }
            public string P19 { get; set; }
            public string P20 { get; set; }
            public string P21 { get; set; }
            public string P22 { get; set; }
            public string P23 { get; set; }
            public string P24 { get; set; }
            public string P25 { get; set; }
            public string P26 { get; set; }
            public string P27 { get; set; }
            public string P28 { get; set; }
            public string P29 { get; set; }
            public string P30 { get; set; }
            public string P31 { get; set; }
            public string P32 { get; set; }
            public string P33 { get; set; }
            public string P34 { get; set; }
            public string P35 { get; set; }
            public string P36 { get; set; }
            public string P37 { get; set; }
            public string P38 { get; set; }
            public string P39 { get; set; }
            public string P40 { get; set; }
            public string P41 { get; set; }
            public string P42 { get; set; }
            public string P43 { get; set; }
            public string P44 { get; set; }
            public string P45 { get; set; }
            public string P46 { get; set; }
            public string P47 { get; set; }
            public string P48 { get; set; }
            public string P49 { get; set; }
            public string P50 { get; set; }
            public string P51 { get; set; }
            public string P52 { get; set; }
            public string P53 { get; set; }
            public string P54 { get; set; }
            public string P55 { get; set; }
            public string P56 { get; set; }
            public string P57 { get; set; }
            public string P58 { get; set; }
            public string P59 { get; set; }
            public string P60 { get; set; }
            public string P61 { get; set; }
            public string P62 { get; set; }
            public string P63 { get; set; }
            public string P64 { get; set; }
            public string P65 { get; set; }
            public string P66 { get; set; }
            public string P67 { get; set; }
            public string P68 { get; set; }
            public string P69 { get; set; }
            public string P70 { get; set; }
            public string P71 { get; set; }
            public string P72 { get; set; }
            public string P73 { get; set; }
            public string P74 { get; set; }
            public string P75 { get; set; }
            public string P76 { get; set; }
            public string P77 { get; set; }
            public string P78 { get; set; }
            public string P79 { get; set; }
            public string P80 { get; set; }
            public string P81 { get; set; }
            public string P82 { get; set; }
            public string P83 { get; set; }
            public string P84 { get; set; }
            public string P85 { get; set; }
            public string P86 { get; set; }
            public string P87 { get; set; }
            public string P88 { get; set; }
            public string P89 { get; set; }
            public string P90 { get; set; }
            public string P91 { get; set; }
            public string P92 { get; set; }
            public string P93 { get; set; }
            public string P94 { get; set; }
            public string P95 { get; set; }
            public string P96 { get; set; }
            public string P97 { get; set; }
            public string P98 { get; set; }
            public string P99 { get; set; }
            public string P100 { get; set; }
        }

        private static LargePayload CreatePayload(int seed)
        {
            // deterministic payload contents
            var rnd = new Random(seed);
            string Next() => rnd.Next().ToString();

            return new LargePayload
            {
                P01 = Next(), P02 = Next(), P03 = Next(), P04 = Next(), P05 = Next(),
                P06 = Next(), P07 = Next(), P08 = Next(), P09 = Next(), P10 = Next(),
                P11 = Next(), P12 = Next(), P13 = Next(), P14 = Next(), P15 = Next(),
                P16 = Next(), P17 = Next(), P18 = Next(), P19 = Next(), P20 = Next(),
                P21 = Next(), P22 = Next(), P23 = Next(), P24 = Next(), P25 = Next(),
                P26 = Next(), P27 = Next(), P28 = Next(), P29 = Next(), P30 = Next(),
                P31 = Next(), P32 = Next(), P33 = Next(), P34 = Next(), P35 = Next(),
                P36 = Next(), P37 = Next(), P38 = Next(), P39 = Next(), P40 = Next(),
                P41 = Next(), P42 = Next(), P43 = Next(), P44 = Next(), P45 = Next(),
                P46 = Next(), P47 = Next(), P48 = Next(), P49 = Next(), P50 = Next(),
                P51 = Next(), P52 = Next(), P53 = Next(), P54 = Next(), P55 = Next(),
                P56 = Next(), P57 = Next(), P58 = Next(), P59 = Next(), P60 = Next(),
                P61 = Next(), P62 = Next(), P63 = Next(), P64 = Next(), P65 = Next(),
                P66 = Next(), P67 = Next(), P68 = Next(), P69 = Next(), P70 = Next(),
                P71 = Next(), P72 = Next(), P73 = Next(), P74 = Next(), P75 = Next(),
                P76 = Next(), P77 = Next(), P78 = Next(), P79 = Next(), P80 = Next(),
                P81 = Next(), P82 = Next(), P83 = Next(), P84 = Next(), P85 = Next(),
                P86 = Next(), P87 = Next(), P88 = Next(), P89 = Next(), P90 = Next(),
                P91 = Next(), P92 = Next(), P93 = Next(), P94 = Next(), P95 = Next(),
                P96 = Next(), P97 = Next(), P98 = Next(), P99 = Next(), P100 = Next(),
            };
        }

        #endregion

        #region Original implementation (copied from NostifyObject)

        /// <summary>
        /// Local copy of the current UpdateProperties implementation for baseline measurement.
        /// </summary>
        private static void OriginalUpdateProperties<T>(NostifyObject target, object payload) where T : NostifyObject
        {
            var nosObjProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetSetMethod() != null)
                .ToList();
            var jPayload = JObject.FromObject(payload);
            var payloadProps = jPayload.Children<JProperty>();

            foreach (JProperty prop in payloadProps)
            {
                OriginalUpdateProperty<T>(target, prop.Name, prop.Name, jPayload, nosObjProps);
            }
        }

        private static void OriginalUpdateProperty<T>(NostifyObject target, string propertyToSet, string propertyToGetValueFrom, JObject jPayload, List<PropertyInfo> thisNostifyObjectProps = null) where T : NostifyObject
        {
            var nosObjProps = thisNostifyObjectProps ?? typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();
            PropertyInfo propToUpdate = nosObjProps.Where(p => p.Name == propertyToSet).SingleOrDefault();
            if (propToUpdate != null)
            {
                var eg = typeof(NostifyExtensions).GetMethod("GetValue");
                var getValueRef = eg.MakeGenericMethod(propToUpdate.PropertyType);
                var valueToSet = getValueRef.Invoke(null, new object[] { jPayload, propertyToGetValueFrom });
                typeof(T).GetProperty(propToUpdate.Name).SetValue(target, valueToSet);
            }
        }

        #endregion

        #region Optimized implementation (proposed)

        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _propertyMapCache = new();

        private static readonly MethodInfo _getValueMethodInfo = typeof(NostifyExtensions)
            .GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);

        /// <summary>
        /// Get or build a dictionary of writable properties for type T keyed by property name.
        /// This is the core reflection cache for the optimized implementation.
        /// </summary>
        private static Dictionary<string, PropertyInfo> GetPropertyMap<T>() where T : NostifyObject
        {
            return _propertyMapCache.GetOrAdd(typeof(T), t =>
            {
                return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetSetMethod() != null)
                    .ToDictionary(p => p.Name, p => p);
            });
        }

        private static void OptimizedUpdateProperties<T>(NostifyObject target, object payload) where T : NostifyObject
        {
            var jPayload = JObject.FromObject(payload);
            var payloadProps = jPayload.Children<JProperty>();
            var propertyMap = GetPropertyMap<T>();

            foreach (JProperty prop in payloadProps)
            {
                OptimizedUpdateProperty<T>(target, prop.Name, prop.Name, jPayload, propertyMap);
            }
        }

        private static void OptimizedUpdateProperty<T>(NostifyObject target, string propertyToSet, string propertyToGetValueFrom, JObject jPayload, Dictionary<string, PropertyInfo> propertyMap) where T : NostifyObject
        {
            if (!propertyMap.TryGetValue(propertyToSet, out var propToUpdate))
            {
                // property does not exist on T, no-op like original
                return;
            }

            // reuse cached MethodInfo for GetValue and only make generic per property type
            var getValueRef = _getValueMethodInfo.MakeGenericMethod(propToUpdate.PropertyType);
            var valueToSet = getValueRef.Invoke(null, new object[] { jPayload, propertyToGetValueFrom });

            // use existing PropertyInfo directly instead of re-querying typeof(T)
            propToUpdate.SetValue(target, valueToSet);
        }

        #endregion

        #region Benchmark harness

        /// <summary>
        /// Simple micro-benchmark: run the original and optimized implementations
        /// across many aggregates and payloads and compare total elapsed time.
        ///
        /// This is not a formal benchmark; it is intended to give a rough
        /// indication of order-of-magnitude improvement.
        /// </summary>
        [Fact]
        public void Compare_original_vs_optimized_UpdateProperties_performance()
        {
            const int aggregateCount = 200;   // number of aggregate instances
            const int iterationsPerAggregate = 200; // number of events per aggregate

            // Warm up JIT and caches
            Warmup();

            var aggregates = Enumerable.Range(0, aggregateCount)
                .Select(_ => new LargeAggregate())
                .ToArray();

            var payloads = Enumerable.Range(0, iterationsPerAggregate)
                .Select(i => (object)CreatePayload(i))
                .ToArray();

            // Measure original
            var originalSw = Stopwatch.StartNew();
            foreach (var aggregate in aggregates)
            {
                foreach (var payload in payloads)
                {
                    OriginalUpdateProperties<LargeAggregate>(aggregate, payload);
                }
            }

            originalSw.Stop();
            var originalMs = originalSw.Elapsed.TotalMilliseconds;

            // Measure optimized
            var optimizedSw = Stopwatch.StartNew();
            foreach (var aggregate in aggregates)
            {
                foreach (var payload in payloads)
                {
                    OptimizedUpdateProperties<LargeAggregate>(aggregate, payload);
                }
            }

            optimizedSw.Stop();
            var optimizedMs = optimizedSw.Elapsed.TotalMilliseconds;

            // Log results to the xUnit output for manual inspection.
            Console.WriteLine($"Original:  {originalMs:n2} ms");
            Console.WriteLine($"Optimized: {optimizedMs:n2} ms");

            // Percent improvement: positive means optimized is faster
            var percentImprovement = (originalMs - optimizedMs) / originalMs * 100.0;
            Console.WriteLine($"Improvement: {percentImprovement:n2}%");

            // Sanity check: ensure optimized is not dramatically slower. We do not
            // assert a specific perf gain to avoid flakiness across environments.
            Assert.True(optimizedMs <= originalMs * 1.10, "Optimized implementation should not be more than 10% slower than original.");
        }

        /// <summary>
        /// One-time warmup so that JIT and caches don't skew baseline too much.
        /// </summary>
        private static void Warmup()
        {
            var aggregate = new LargeAggregate();
            var payload = (object)CreatePayload(0);

            OriginalUpdateProperties<LargeAggregate>(aggregate, payload);
            OptimizedUpdateProperties<LargeAggregate>(aggregate, payload);
        }

        #endregion
    }
}
