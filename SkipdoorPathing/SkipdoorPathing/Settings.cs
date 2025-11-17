using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SkipdoorPathing
{
    public class Settings : ModSettings
    {
        public bool CanAnimalsUseSkipdoors = false;
        public bool BlockComplexJobs = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref CanAnimalsUseSkipdoors, "canAnimalsUseSkipdoors", false);
            Scribe_Values.Look(ref BlockComplexJobs, "BlockComplexJobs", true);
            base.ExposeData();
        }
    }
}
