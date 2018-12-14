using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace SettingsTemplates.Settings
{
    public abstract class XmlSettings
    {
        private static string _path;
        protected XmlSettings(string path)
        {
            _path = path;                       
        }

        public void Load()
        {
            if (File.Exists(_path))
            {
                LoadFromXml(XDocument.Load(_path));                               
            }
            else
            {
                LoadDefaults();
            }
        }

        public void Save()
        {
            
        }

        private void LoadFromXml(XDocument document)
        {
            if (document.Root == null || document.Root.Name != GetType().Name)
            {                
                throw new Exception("Corrupted xml settings file");
            }
            var elements = document.Root.Descendants().ToList();
            foreach (var field in GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => Attribute.IsDefined(x, typeof(SettingsNodeAttribute))))
            {
                LoadElement(field, field.FieldType, elements);
            }


            foreach (var property in GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                             BindingFlags.NonPublic | BindingFlags.GetProperty |
                                                             BindingFlags.SetProperty)
                .Where(x => Attribute.IsDefined(x, typeof(DefaultValueAttribute))))
            {
                LoadElement(property, property.PropertyType, elements);
            }

        }

        private void LoadElement(MemberInfo field, Type type, List<XElement> elements)
        {
            var customAttributes = field.GetCustomAttributes(typeof(SettingsNodeAttribute), false);
            var customAttribute = (SettingsNodeAttribute) customAttributes[0];
            var elementName = customAttribute.ElementName ?? field.Name;
            var loader = customAttribute.Loader;
            var elementNameParts = elementName.Split('.');
            var element = elements.FirstOrDefault(x => x.Name == elementNameParts[0]);
            for (var i = 1; i < elementNameParts.Length; i++)
            {
                if (element == null)
                {
                    break;                            
                }
                element = element.Descendants().FirstOrDefault(x => x.Name == elementNameParts[i]);
            }
            if (element != null)
            {
                var value = element.Value;
                object obj;
                if (loader != null)
                {
                    obj = GetType().GetMethod(loader, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new[]{(object)element});
                }
                else if (type.IsArray || type.IsGenericType)
                {
                    Type elementType;
                    if (type.IsArray)
                    {
                        elementType = type.GetElementType();
                    }
                    else
                    {
                        var elementTypes = type.GetGenericArguments();
                        if (elementTypes.Length > 1)
                        {                                
                            throw new NotSupportedException("XmlSettings supports only single generics");
                        }
                        elementType = elementTypes[0]; 
                    }
                    
                    List<object> objects;
                    if (element.Descendants().Any())
                    {
                        objects = element.Descendants().Select(descendant =>
                            ConvertType(descendant, elementType, $"Descendant of {elementName}")).ToList();
                    }
                    else
                    {
                        objects = value.Split(' ', ',').Select(el => ConvertType(el, elementType, $"Entry in {elementName}")).ToList();
                    }                        
                    if (type.IsArray)
                    {
                        obj = objects.ToArray();
                    }
                    else
                    {
                        var collection = Activator.CreateInstance(type);                            
                        if (collection.GetType().GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>)))
                        {
                            foreach (var o in objects)
                            {
                                collection.GetType().GetMethod("Add")?.Invoke(collection, new[] {o});
                            }
                            obj = collection;   
                        }
                        else
                        {
                            throw new NotSupportedException("XmlSettings supports only ICollection generics");
                        }
                    }
                }
                else
                {
                    obj = ConvertType(element, type, elementName);
                }

                (field as FieldInfo)?.SetValue(this, obj);
                (field as PropertyInfo)?.SetValue(this, obj);                
            } 
            else 
            {
                if (Attribute.IsDefined(field, typeof(DefaultValueAttribute)))
                {
                    customAttributes = field.GetCustomAttributes(typeof(DefaultValueAttribute), false);
                    var defaultValueAttribute = (DefaultValueAttribute)customAttributes[0];                    
                    (field as FieldInfo)?.SetValue(this, defaultValueAttribute.Value);
                    (field as PropertyInfo)?.SetValue(this, defaultValueAttribute.Value);
                }
                else
                {                        
                    throw new Exception($"Corrupted xml setting file: missed '{elementName}' node");
                }                     
            }
        }

        public void LoadDefaults()
        {
            foreach (var field in GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => Attribute.IsDefined(x, typeof(DefaultValueAttribute))))
            {
                var customAttributes = field.GetCustomAttributes(typeof(DefaultValueAttribute), false);
                var defaultValueAttribute = (DefaultValueAttribute)customAttributes[0];
                field.SetValue(this, defaultValueAttribute.Value);
            }

            foreach (var property in GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty)
                .Where(x => Attribute.IsDefined(x, typeof(DefaultValueAttribute))))
            {
                var customAttributes = property.GetCustomAttributes(typeof(DefaultValueAttribute), false);
                var defaultValueAttribute = (DefaultValueAttribute)customAttributes[0];
                property.SetValue(this, defaultValueAttribute.Value);
            }
        }
        
        private object ConvertType(XElement element, Type t, string elmName)
        {
            object obj;
            try
            {
                var method = t.GetMethod("LoadFromXml", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    obj = method.Invoke(null, new[]{(object)element});
                }
                else
                {
                    var underlyingType = Nullable.GetUnderlyingType(t);
                    if (underlyingType == null)
                    {
                        obj = t.IsEnum ? Enum.Parse(t, element.Value) : Convert.ChangeType(element.Value, t, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        obj = ConvertType(element, underlyingType, elmName);
                    }   
                }                
            }
            catch
            {                
                throw new Exception($"Setting {elmName} has invalid value \"{element.Value}\"!");
            }
            return obj;
        }
        private object ConvertType(string value, Type t, string elmName)
        {
            object obj;
            try
            {
                var underlyingType = Nullable.GetUnderlyingType(t);
                if (underlyingType == null)
                {
                    obj = t.IsEnum ? Enum.Parse(t, value) : Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
                }
                else
                {
                    obj = ConvertType(value, underlyingType, elmName);
                }               
            }
            catch
            {                
                throw new Exception($"Setting {elmName} has invalid value \"{value}\"!");
            }
            return obj;
        }
    }
}