﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NiceHashMiner.Configs.Data;

namespace NHM.Wpf.ViewModels.Settings
{
    public class AdvancedGeneralVM : SettingsBaseVM
    {
        public AdvancedGeneralVM(GeneralConfig confObj) 
            : base(confObj, "General")
        { }
    }
}