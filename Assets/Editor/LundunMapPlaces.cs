// V2b: lets the map drawing see the R7 plots (hospitals, police stations, safehouses), so they show on the map.
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        /// The planned place plots. Call after Generator.Build(world).
        public static List<PlaceSite> MapPlaces() { return PlanPlaces(); }
    }
}
