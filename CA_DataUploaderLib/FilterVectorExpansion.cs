#nullable enable
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class FilterVectorExpansion
    {
        private readonly List<IOconfFilter> _decisionFilters;
        private readonly List<FilterSample> _legacyInputFilters;
        private readonly CALogLevel _logLevel;

        /// <remarks>
        /// <paramref name="inputs"/> is updated by adding classic input filters and removing hidden sources.
        /// </remarks>
        public FilterVectorExpansion(List<VectorDescriptionItem> inputs, Func<IEnumerable<IOconfFilter>> getFilters, CALogLevel logLevel)
        {
            _logLevel = logLevel;
            var allfilters = getFilters().ToList();
            _decisionFilters = allfilters.Where(f => f.IsDecisionFilter).ToList();
            _legacyInputFilters = GetLegacyFilters();
            inputs.AddRange(_legacyInputFilters.Select(m => new VectorDescriptionItem("double", m.Output.Name, DataTypeEnum.Input)));
            RemoveLegacyFiltersHiddenSources(inputs, i => i.Descriptor);

            List<FilterSample> GetLegacyFilters()
            {
                var filters = allfilters.Where(f => !f.IsDecisionFilter).ToList();
                var filtersWithoutItem = filters
                    .SelectMany(f => f.SourceNames.Select(s => new { Filter = f, Source = s }))
                    .Where(f => !inputs.Any(i => i.Descriptor == f.Source))
                    .ToList();
                if (filtersWithoutItem.Count == 0)
                    return filters.Select(x => new FilterSample(x)).ToList();

                var names = string.Join(',', filtersWithoutItem.Select(f => f.Filter.Name).Distinct());
                var sources = string.Join(',', filtersWithoutItem.Select(f => f.Source).Distinct());
                throw new InvalidOperationException($"Misconfigured filters detected.{Environment.NewLine}Filters: {names}{Environment.NewLine}Missing sources: {sources}");
            }
        }

        public IEnumerable<VectorDescriptionItem> GetDecisionVectorDescriptionEntries() => 
            _decisionFilters.SelectMany(f => f.GetDecisionFields().Select(n => new VectorDescriptionItem("double", n, DataTypeEnum.State)));
        /// <param name="vectorFields">all the vector fields, including those returned by <see cref="GetDecisionVectorDescriptionEntries"/></param>
        public void Initialize(IEnumerable<string> vectorFields)
        {
            var fields = vectorFields.ToList();
            foreach (var filter in _decisionFilters)
                filter.Initialize(fields);
        }

        /// <param name="vector">the full vector with the same fields as specified in <see cref="Initialize"/> (with only the inputs updated in this cycle)</param>
        public void Apply(MathVectorExpansion.MathContext context)
        {
            foreach (var filter in _decisionFilters)
                filter.MakeDecision(context);
        }

        /// <remarks>
        /// <paramref name="inputs"/> is updated by adding classic input filters and removing hidden sources.
        /// </remarks>
        public void ApplyLegacyFilters(List<SensorSample> inputs)
        {
            //removing allocations here is tricky, but we should consider keeping the original inputs in the full vector and when hiding sources we set those inputs as not uploadable.
            //note that it also helps cross checking full vectors, if one also has: the previous vector, the state of filters (they have a queue of values within filter length), events in the cycle.
            foreach (var filter in _legacyInputFilters)
            {
                filter.Input(inputs);
                inputs.Add(filter.Output);
            }

            RemoveLegacyFiltersHiddenSources(inputs, i => i.Name);
        }

        private void RemoveLegacyFiltersHiddenSources<T>(List<T> list, Func<T, string> getEntryName)
        {
            if (_logLevel == CALogLevel.Debug)
                return;

            foreach (var filter in _legacyInputFilters)
            {
                if (!filter.Filter.HideSource) continue;
                list.RemoveAll(vd => filter.HasSource(getEntryName(vd)));
            }
        }
    }
}
