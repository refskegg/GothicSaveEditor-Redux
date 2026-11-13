using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace GothicSaveEditor.Models
{
    public class Script
    {
        public string Name { get; set; }
        public Dictionary<string, int> actions = new();
        private readonly Action<Script> _executeVoid;

        public IRelayCommand ImportScriptCommand { get; }

        public Script(string name, Dictionary<string, int> actions, Action<Script> executeVoid)
        {
            Name = name;
            this.actions = actions;
            _executeVoid = executeVoid;
            ImportScriptCommand = new RelayCommand(() => Task.Run(() => _executeVoid(this)));
        }
    }
}
