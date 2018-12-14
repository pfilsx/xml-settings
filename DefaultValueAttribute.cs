using System;

namespace SettingsTemplates.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class DefaultValueAttribute : Attribute
    {
        public readonly object Value;

        public DefaultValueAttribute(object val)
        {
            Value = val;
        }
    }
}