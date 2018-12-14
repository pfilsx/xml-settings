using System;

namespace SettingsTemplates.Settings
{    
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SettingsNodeAttribute : Attribute
    {
        public string ElementName;

        public string Loader;

        public string Saver;

        public SettingsNodeAttribute()
        {
        }

        public SettingsNodeAttribute(string elementName, string loader = null, string saver = null)
        {
            ElementName = elementName;
            Loader = loader;
            Saver = saver;
        }
    }
}