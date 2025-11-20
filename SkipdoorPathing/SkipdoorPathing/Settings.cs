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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref CanAnimalsUseSkipdoors, "canAnimalsUseSkipdoors", false);
            base.ExposeData();
        }
    }
}
