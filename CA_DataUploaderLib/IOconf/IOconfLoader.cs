#nullable enable
using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLoader : IIOconfLoader
    {
        private readonly List<(string rowType, Func<string, int, IOconfRow> loader)> Loaders = new()
        {
            ("LoopName", (r, l) => new IOconfLoopName(r, l)),
            ("Account", (r, l) => new IOconfAccount(r, l)),
            ("SampleRates", (r, l) => new IOconfSamplingRates(r, l)),
            ("Map", (r, l) => new IOconfMap(r, l)),
            ("Math", (r, l) => new IOconfMath(r, l)),
            ("Alert", (r, l) => new IOconfAlert(r, l)),
            (IOconfTemp.TypeKName, IOconfTemp.NewTypeK),
            (IOconfTemp.TypeJName, IOconfTemp.NewTypeJ),
            ("Heater", (r, l) => new IOconfHeater(r, l)),
            ("Oven", (r, l) => new IOconfOven(r, l)),
            (IOconfOvenProportionalControlUpdates.TypeName, (r, l) => new IOconfOvenProportionalControlUpdates(r, l)),
            ("Filter", (r, l) => new IOconfFilter(r, l)),
            ("RPiTemp", (r, l) => new IOconfRPiTemp(r, l)),
            ("GenericSensor", (r, l) => new IOconfGeneric(r, l)),
            ("GenericOutput", (r, l) => new IOconfGenericOutput(r, l)),
            ("SwitchboardSensor", (r, l) => new IOconfSwitchboardSensor(r, l)),
            ("Node", (r, l) => new IOconfNode(r, l)),
            ("Code", (r, l) => new IOconfCode(r, l)),
            (IOconfCurrent.TypeName, (r, l) => new IOconfCurrent(r, l)),
            (IOconfCurrentFault.TypeName, (r, l) => new IOconfCurrentFault(r, l)),
        };

        public void AddLoader(string rowType, Func<string, int, IOconfRow> loader)
        {
            if (GetLoader(rowType) != null)
                throw new ArgumentException($"The specified loader rowType is already in use: {rowType}", nameof(rowType));

            Loaders.Add((rowType, loader));
        }

        public Func<string, int, IOconfRow>? GetLoader(ReadOnlySpan<char> rowType)
        {
            foreach (var loader in Loaders)
                if (rowType.Equals(loader.rowType, StringComparison.InvariantCultureIgnoreCase))
                    return loader.loader;

            return null;
        }
    }
}