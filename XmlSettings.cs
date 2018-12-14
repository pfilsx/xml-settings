using System;
using System.Collections;
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
            var xml = GetXml();
            xml.Save(_path);
        }
        
        public void LoadDefaults()
        {
            var type = GetType();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => Attribute.IsDefined(x, typeof(DefaultValueAttribute))))
            {
                var customAttributes = field.GetCustomAttributes(typeof(DefaultValueAttribute), false);
                var defaultValueAttribute = (DefaultValueAttribute)customAttributes[0];
                field.SetValue(this, defaultValueAttribute.Value);
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty)
                .Where(x => Attribute.IsDefined(x, typeof(DefaultValueAttribute))))
            {
                var customAttributes = property.GetCustomAttributes(typeof(DefaultValueAttribute), false);
                var defaultValueAttribute = (DefaultValueAttribute)customAttributes[0];
                property.SetValue(this, defaultValueAttribute.Value);
            }
        }

        private void LoadFromXml(XDocument document)
        {
            var type = GetType();
            if (document.Root == null || document.Root.Name != type.Name)
            {                
                throw new Exception("Corrupted xml settings file");
            }
            var elements = document.Root.Descendants().ToList();            
            foreach (var field in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty)
                .Where(x => Attribute.IsDefined(x, typeof(SettingsNodeAttribute))))
            {
                LoadElement(field, elements);
            }
        }
        private void LoadElement(MemberInfo field, List<XElement> elements)
        {            
            var type = (field as FieldInfo)?.FieldType ?? (field as PropertyInfo)?.PropertyType;
            if (type == null)
            {
                throw new Exception("SettingsAttribute must be a property or field");
            }
            var customAttributes = field.GetCustomAttributes(typeof(SettingsNodeAttribute), false);
            var customAttribute = (SettingsNodeAttribute) customAttributes[0];
            var elementName = customAttribute.ElementName ?? field.Name;
            var loader = customAttribute.Loader;            
            var element = elements.FirstOrDefault(x => x.Name == elementName);            
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
                        var collectionType = collection.GetType();
                        if (collectionType.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>)))
                        {
                            foreach (var o in objects)
                            {
                                collectionType.GetMethod("Add")?.Invoke(collection, new[] {o});
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

        private XElement GetXml()
        {
            var type = GetType();
            var root = new XElement(type.Name);
            foreach (var field in type.GetMembers(BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic | BindingFlags.GetProperty |
                                                        BindingFlags.SetProperty)
                .Where(x => Attribute.IsDefined(x, typeof(SettingsNodeAttribute))))
            {
                var element = GetElementXml(field);
                root.Add(element);
            }
            return root;
        }

        private XElement GetElementXml(MemberInfo field)
        {
            var type = (field as FieldInfo)?.FieldType ?? (field as PropertyInfo)?.PropertyType;
            var customAttributes = field.GetCustomAttributes(typeof(SettingsNodeAttribute), false);
            var customAttribute = (SettingsNodeAttribute) customAttributes[0];
            var elementName = customAttribute.ElementName ?? field.Name;
            var saver = customAttribute.Saver;
            var element = new XElement(elementName);
            var value = (field as PropertyInfo)?.GetValue(this, null) ?? (field as FieldInfo)?.GetValue(this);
            if (value != null)
            {
                if (saver != null)
                {
                    var args = new[] {element, value};
                    GetType().GetMethod(saver, BindingFlags.Public | BindingFlags.Static)
                        ?.Invoke(null, args);
                } else if (type.IsArray)
                {                
                    element.Value = string.Join(", ", (Array) value);
                } 
                else if (type.IsGenericType)
                {                    
                    if (type.GetGenericArguments().Length > 1)
                    {                                
                        throw new NotSupportedException("XmlSettings supports only single generics");
                    }
                    if (value.GetType().GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>)))
                    {                        
                        foreach (var o in (IEnumerable) value)
                        {
                            element.Add(new XElement("Entry"){Value = o.ToString()});
                        }
                    }                
                }
                else
                {
                    var method = type.GetMethod("GetXml", BindingFlags.Public | BindingFlags.Static);
                    if (method != null)
                    {
                        var args = new[] {element, value};
                        method.Invoke(null, args);
                    }
                    else
                    {
                        element.Value = value.ToString();
                    }                    
                }   
            }            
            return element;
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