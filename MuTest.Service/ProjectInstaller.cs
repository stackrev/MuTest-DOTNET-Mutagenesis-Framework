﻿using System.ComponentModel;
using System.Configuration.Install;

namespace MuTest.Service
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
        }
    }
}
