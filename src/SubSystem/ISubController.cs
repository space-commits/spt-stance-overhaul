using RealismCommonLib.ModifierHandlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StanceOverhaul.SubSystem
{
    public interface ISubSystem
    {
        public void RunOnAwake();

        public void RunOnDestroy();

        public void RunOnUpdate(float deltaTime);
    }
}
