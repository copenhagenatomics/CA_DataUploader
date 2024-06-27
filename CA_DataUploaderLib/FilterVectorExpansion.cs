#nullable enable
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib;

public class FilterVectorExpansion
{
    private readonly List<IOconfFilter.DecisionFilter> _decisionFilters;
    private readonly List<LegacyFilterSample> _legacyInputFilters;
    private readonly CALogLevel _logLevel;

    /// <remarks>
    /// <paramref name="inputs"/> is updated by adding legacy input filters and removing hidden sources.
    /// </remarks>
    public FilterVectorExpansion(List<VectorDescriptionItem> inputs, IEnumerable<IOconfFilter> configuredFilters, CALogLevel logLevel)
    {
        _logLevel = logLevel;
        var allfilters = configuredFilters.ToList();
        _decisionFilters = IOconfFilter.DecisionFilters(allfilters);
        _legacyInputFilters = IOconfFilter.LegacyFilters(allfilters);
        ValidateLegacyFiltersSources();
        inputs.AddRange(_legacyInputFilters.Select(m => new VectorDescriptionItem("double", m.Output.Name, DataTypeEnum.Input)));
        RemoveLegacyFiltersHiddenSources(inputs, i => i.Descriptor);

        void ValidateLegacyFiltersSources()
        {
            var filtersWithInputsCounts = _legacyInputFilters
                .SelectMany(f => f.SourceNames.Select(s => new { Filter = f, Source = s, InputsCount = inputs.Count(i => i.Descriptor == s)}))
                .ToList();
            var filtersWithoutSource = filtersWithInputsCounts.Where(f => f.InputsCount == 0).ToList();
            if (filtersWithoutSource.Count > 0)
            {
                var names = string.Join(',', filtersWithoutSource.Select(f => f.Filter.Name).Distinct());
                var sources = string.Join(',', filtersWithoutSource.Select(f => f.Source).Distinct());
                throw new InvalidOperationException($"Misconfigured filters detected.{Environment.NewLine}Filters: {names}{Environment.NewLine}Missing sources: {sources}");
            }

            var filtersWithDuplicateSources = filtersWithInputsCounts.Where(f => f.InputsCount > 2).ToList();
            if (filtersWithDuplicateSources.Count > 0)
            {
                var names = string.Join(',', filtersWithDuplicateSources.Select(f => f.Filter.Name).Distinct());
                var sources = string.Join(',', filtersWithDuplicateSources.Select(f => f.Source).Distinct());
                throw new InvalidOperationException($"Misconfigured filters detected.{Environment.NewLine}Filters: {names}{Environment.NewLine}Sources without unique inputs names: {sources}");
            }
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

    /// <param name="vector">the context contains the full vector with the same fields as specified in <see cref="Initialize"/> (with only the inputs updated in this cycle)</param>
    public void Apply(MathVectorExpansion.MathContext context)
    {
        foreach (var filter in _decisionFilters)
            filter.MakeDecision(context);
    }

    /// <remarks>
    /// <paramref name="inputs"/> is updated by adding legacy input filters and removing hidden sources.
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
            if (!filter.HideSource) continue;
            list.RemoveAll(vd => filter.HasSource(getEntryName(vd)));
        }
    }
}
