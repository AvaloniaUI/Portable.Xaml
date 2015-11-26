//
// Copyright (C) 2010 Novell Inc. http://novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections;
using System.Collections.Generic;
using Portable.Xaml.ComponentModel;
using System.Linq;
using System.Reflection;
using Portable.Xaml.Markup;
using Portable.Xaml.Schema;
using System.Xml.Serialization;
using System.Collections.ObjectModel;

namespace Portable.Xaml
{
	public class XamlType : IEquatable<XamlType>
	{
		Type type, underlying_type;
		string explicit_ns;
		XamlType base_type;
		XamlTypeInvoker invoker;
		Dictionary<XamlDirective, XamlMember> aliased_property_cache;
		List<XamlMember> all_members_cache;
		List<XamlMember> all_attachable_members_cache;
		XamlCollectionKind? collectionKind;
		bool gotContentProperty;
		XamlMember contentProperty;
		TypeAttributeProvider attributeProvider;
		bool? isAmbient;
		XamlType itemType;
		bool gotTypeConverter;
		XamlValueConverter<TypeConverter> cachedTypeConverter;

		public XamlType (Type underlyingType, XamlSchemaContext schemaContext)
			: this (underlyingType, schemaContext, null)
		{
		}

//		static readonly Type [] predefined_types = {
//				typeof (XData), typeof (Uri), typeof (TimeSpan), typeof (PropertyDefinition), typeof (MemberDefinition), typeof (Reference)
//			};

		public XamlType (Type underlyingType, XamlSchemaContext schemaContext, XamlTypeInvoker invoker)
			: this (schemaContext, invoker)
		{
			if (underlyingType == null)
				throw new ArgumentNullException ("underlyingType");
			type = underlyingType;
			underlying_type = type;

			XamlType xt;
			if (XamlLanguage.InitializingTypes) {
				// These are special. Only XamlLanguage members are with shorthand name.
				if (type == typeof (PropertyDefinition))
					Name = "Property";
				else if (type == typeof (MemberDefinition))
					Name = "Member";
				else
					Name = GetXamlName (type);
				PreferredXamlNamespace = XamlLanguage.Xaml2006Namespace;
			} else if ((xt = XamlLanguage.AllTypes.FirstOrDefault (t => t.UnderlyingType == type)) != null) {
				Name = xt.Name;
				PreferredXamlNamespace = XamlLanguage.Xaml2006Namespace;
			} else {
				Name = GetXamlName (type);
				PreferredXamlNamespace = schemaContext.GetXamlNamespace (type.Namespace) ?? String.Format ("clr-namespace:{0};assembly={1}", type.Namespace, type.GetTypeInfo().Assembly.GetName ().Name);
			}
			if (type.GetTypeInfo().IsGenericType) {
				TypeArguments = new List<XamlType> ();
				foreach (var gta in type.GetTypeInfo().GetGenericArguments())
					TypeArguments.Add (schemaContext.GetXamlType (gta));
			}
		}

		public XamlType (string unknownTypeNamespace, string unknownTypeName, IList<XamlType> typeArguments, XamlSchemaContext schemaContext)
			: this (schemaContext, null)
		{
			if (unknownTypeNamespace == null)
				throw new ArgumentNullException ("unknownTypeNamespace");
			if (unknownTypeName == null)
				throw new ArgumentNullException ("unknownTypeName");
			if (schemaContext == null)
				throw new ArgumentNullException ("schemaContext");

			type = typeof (object);
			Name = unknownTypeName;
			PreferredXamlNamespace = unknownTypeNamespace;
			TypeArguments = typeArguments != null && typeArguments.Count == 0 ? null : typeArguments;
			explicit_ns = unknownTypeNamespace;
		}

		protected XamlType (string typeName, IList<XamlType> typeArguments, XamlSchemaContext schemaContext)
			: this (String.Empty, typeName, typeArguments, schemaContext)
		{
		}

		XamlType (XamlSchemaContext schemaContext, XamlTypeInvoker invoker)
		{
			if (schemaContext == null)
				throw new ArgumentNullException ("schemaContext");
			SchemaContext = schemaContext;
			this.invoker = invoker ?? new XamlTypeInvoker (this);
		}

		// populated properties

		internal EventHandler<XamlSetMarkupExtensionEventArgs> SetMarkupExtensionHandler {
			get { return LookupSetMarkupExtensionHandler (); }
		}

		internal EventHandler<XamlSetTypeConverterEventArgs> SetTypeConverterHandler {
			get { return LookupSetTypeConverterHandler (); }
		}

		public IList<XamlType> AllowedContentTypes {
			get { return LookupAllowedContentTypes (); }
		}

		public XamlType BaseType {
			get { return LookupBaseType (); }
		}

		public bool ConstructionRequiresArguments {
			get { return LookupConstructionRequiresArguments (); }
		}

		public XamlMember ContentProperty {
			get { return LookupContentProperty (); }
		}

		public IList<XamlType> ContentWrappers {
			get { return LookupContentWrappers (); }
		}

		public XamlValueConverter<XamlDeferringLoader> DeferringLoader {
			get { return LookupDeferringLoader (); }
		}

		public XamlTypeInvoker Invoker {
			get { return LookupInvoker (); }
		}

		public bool IsAmbient {
			get { return LookupIsAmbient (); }
		}

		public bool IsArray {
			get { return LookupCollectionKind () == XamlCollectionKind.Array; }
		}

		// it somehow treats array as not a collection...
		public bool IsCollection {
			get { return LookupCollectionKind () == XamlCollectionKind.Collection; }
		}

		public bool IsConstructible {
			get { return LookupIsConstructible (); }
		}

		public bool IsDictionary {
			get { return LookupCollectionKind () == XamlCollectionKind.Dictionary; }
		}

		public bool IsGeneric {
			get { return type.GetTypeInfo().IsGenericType; }
		}

		public bool IsMarkupExtension {
			get { return LookupIsMarkupExtension (); }
		}
		public bool IsNameScope {
			get { return LookupIsNameScope (); }
		}
		public bool IsNameValid {
			get { return XamlLanguage.IsValidXamlName (Name); }
		}

		public bool IsNullable {
			get { return LookupIsNullable (); }
		}

		public bool IsPublic {
			get { return LookupIsPublic (); }
		}

		public bool IsUnknown {
			get { return LookupIsUnknown (); }
		}

		public bool IsUsableDuringInitialization {
			get { return LookupUsableDuringInitialization (); }
		}

		public bool IsWhitespaceSignificantCollection {
			get { return LookupIsWhitespaceSignificantCollection (); }
		}

		public bool IsXData {
			get { return LookupIsXData (); }
		}

		public XamlType ItemType {
			get { return LookupItemType (); }
		}

		public XamlType KeyType {
			get { return LookupKeyType (); }
		}

		public XamlType MarkupExtensionReturnType {
			get { return LookupMarkupExtensionReturnType (); }
		}

		public string Name { get; private set; }

		public string PreferredXamlNamespace { get; private set; }

		public XamlSchemaContext SchemaContext { get; private set; }

		public bool TrimSurroundingWhitespace {
			get { return LookupTrimSurroundingWhitespace (); }
		}

		public IList<XamlType> TypeArguments { get; private set; }

		public XamlValueConverter<TypeConverter> TypeConverter {
			get { return LookupTypeConverter (); }
		}

		public Type UnderlyingType {
			get { return LookupUnderlyingType (); }
		}

		public XamlValueConverter<ValueSerializer> ValueSerializer {
			get { return LookupValueSerializer (); }
		}

		internal string GetInternalXmlName ()
		{
			if (IsMarkupExtension && Name.EndsWith ("Extension", StringComparison.Ordinal))
				return Name.Substring (0, Name.Length - 9);
			var stn = XamlLanguage.SpecialNames.FirstOrDefault (s => s.Type == this);
			return stn != null ? stn.Name : Name;
		}

		public static bool operator == (XamlType left, XamlType right)
		{
			return IsNull (left) ? IsNull (right) : left.Equals (right);
		}

		static bool IsNull (XamlType a)
		{
			return Object.ReferenceEquals (a, null);
		}

		public static bool operator != (XamlType left, XamlType right)
		{
			return !(left == right);
		}
		
		public bool Equals (XamlType other)
		{
			// It does not compare XamlSchemaContext.
			return !IsNull (other) &&
				UnderlyingType == other.UnderlyingType &&
				Name == other.Name &&
				PreferredXamlNamespace == other.PreferredXamlNamespace && TypeArguments.ListEquals (other.TypeArguments);
		}

		public override bool Equals (object obj)
		{
			var a = obj as XamlType;
			return Equals (a);
		}
		
		public override int GetHashCode ()
		{
			if (UnderlyingType != null)
				return UnderlyingType.GetHashCode ();
			int x = Name.GetHashCode () << 7 + PreferredXamlNamespace.GetHashCode ();
			if (TypeArguments != null)
				foreach (var t in TypeArguments)
					x = t.GetHashCode () + x << 5;
			return x;
		}

		public override string ToString ()
		{
			return new XamlTypeName (this).ToString ();
			//return String.IsNullOrEmpty (PreferredXamlNamespace) ? Name : String.Concat ("{", PreferredXamlNamespace, "}", Name);
		}

		internal bool CanConvertFrom (XamlType inputType)
		{
			if (CanAssignFrom (inputType))
				return true;

			var tc = TypeConverter;
			if (tc != null) {
				return tc.ConverterInstance.CanConvertFrom (inputType?.UnderlyingType ?? typeof(object));
			}

			return false;
		}

		internal bool CanAssignFrom (XamlType inputType)
		{
			return inputType.CanAssignTo (this);
		}

		public virtual bool CanAssignTo (XamlType xamlType)
		{
			if (xamlType == null)
				return false;
			if (IsUnknown && xamlType.IsUnknown)
				return Equals(xamlType);

			if (UnderlyingType == null)
				return xamlType == XamlLanguage.Object;
			var ut = (xamlType.UnderlyingType ?? typeof (object)).GetTypeInfo();

			// if we are assigning to a nullable type, we allow null
			if (ut.IsValueType 
				&& ut.IsGenericType
				&& ut.GetGenericTypeDefinition () == typeof(Nullable<>)
				&& this == XamlLanguage.Null)
				return true;

			return ut.IsAssignableFrom (UnderlyingType.GetTypeInfo());
		}

		public XamlMember GetAliasedProperty (XamlDirective directive)
		{
			return LookupAliasedProperty (directive);
		}

		public ICollection<XamlMember> GetAllAttachableMembers ()
		{
			return new List<XamlMember> (LookupAllAttachableMembers ());
		}

		public ICollection<XamlMember> GetAllMembers ()
		{
			return new List<XamlMember> (LookupAllMembers ());
		}

		public XamlMember GetAttachableMember (string name)
		{
			return LookupAttachableMember (name);
		}

		public XamlMember GetMember (string name)
		{
			return LookupMember (name, true);
		}

		public IList<XamlType> GetPositionalParameters (int parameterCount)
		{
			return LookupPositionalParameters (parameterCount);
		}

		public virtual IList<string> GetXamlNamespaces ()
		{
			// not quite sure if this is correct, but matches documentation to get all namespaces that type exists in
			var list = new List<string>();

			if (!string.IsNullOrEmpty (explicit_ns))
				list.Add (explicit_ns);

			if (type != null) {
				// type always exists in clr namespace
				list.Add (string.Format ("clr-namespace:{0};assembly={1}", type.Namespace, type.GetTypeInfo ().Assembly.GetName ().Name));

				// check if it's a built-in type
				if (XamlLanguage.AllTypes.Any (r => r.UnderlyingType == type)) {
					list.Add (XamlLanguage.Xaml2006Namespace);
				}

				// check all other registered namespaces (may duplicate for built-in types, such as xaml markup extensions)
				foreach (var ns in SchemaContext.GetAllXamlNamespaces()) {
					if (SchemaContext.GetAllXamlTypes (ns).Any (r => r.UnderlyingType == type)) {
						list.Add (ns);
					}
				}
			}
			if (list.Count == 0)
				return new [] { string.Empty };

			return new ReadOnlyCollection<string> (list);
		}

		// lookups

		protected virtual XamlMember LookupAliasedProperty (XamlDirective directive)
		{
			XamlMember member = null;
			if (aliased_property_cache == null)
				aliased_property_cache = new Dictionary<XamlDirective, XamlMember> ();
			else if (aliased_property_cache.TryGetValue(directive, out member))
				return member;

			if (directive == XamlLanguage.Key) {
				var a = this.GetCustomAttribute<DictionaryKeyPropertyAttribute> ();
				member = a != null ? GetMember (a.Name) : null;
			}
			else if (directive == XamlLanguage.Name) {
				var a = this.GetCustomAttribute<RuntimeNamePropertyAttribute> ();
				member = a != null ? GetMember (a.Name) : null;
			}
			else if (directive == XamlLanguage.Uid) {
				var a = this.GetCustomAttribute<UidPropertyAttribute> ();
				member = a != null ? GetMember (a.Name) : null;
			}
			else if (directive == XamlLanguage.Lang) {
				var a = this.GetCustomAttribute<XmlLangPropertyAttribute> ();
				member = a != null ? GetMember (a.Name) : null;
			}
			aliased_property_cache[directive] = member;
			return member;
		}

		protected virtual IEnumerable<XamlMember> LookupAllAttachableMembers ()
		{
			if (UnderlyingType == null)
				return BaseType != null ? BaseType.LookupAllAttachableMembers () : empty_array;
			if (all_attachable_members_cache == null) {
				all_attachable_members_cache = new List<XamlMember> (DoLookupAllAttachableMembers ());
				all_attachable_members_cache.Sort (TypeExtensionMethods.CompareMembers);
			}
			return all_attachable_members_cache;
		}

		IEnumerable<XamlMember> DoLookupAllAttachableMembers ()
		{
			// based on http://msdn.microsoft.com/en-us/library/ff184560.aspx

			var gl = new Dictionary<string,MethodInfo> ();
			var sl = new Dictionary<string,MethodInfo> ();
			var al = new Dictionary<string,MethodInfo> ();
			//var rl = new Dictionary<string,MethodInfo> ();
			var nl = new List<string> ();
			foreach (var mi in UnderlyingType.GetRuntimeMethods()) {
				if (!mi.IsStatic)
					continue;
				string name = null;
				if (mi.Name.StartsWith ("Get", StringComparison.Ordinal)) {
					if (mi.ReturnType == typeof (void))
						continue;
					var args = mi.GetParameters ();
					if (args.Length != 1)
						continue;
					name = mi.Name.Substring (3);
					gl.Add (name, mi);
				} else if (mi.Name.StartsWith ("Set", StringComparison.Ordinal)) {
					// looks like the return type is *ignored*
					//if (mi.ReturnType != typeof (void))
					//	continue;
					var args = mi.GetParameters ();
					if (args.Length != 2)
						continue;
					name = mi.Name.Substring (3);
					sl.Add (name, mi);
				} else if (mi.Name.EndsWith ("Handler", StringComparison.Ordinal)) {
					var args = mi.GetParameters ();
					if (args.Length != 2)
						continue;
					if (mi.Name.StartsWith ("Add", StringComparison.Ordinal)) {
						name = mi.Name.Substring (3, mi.Name.Length - 3 - 7);
						al.Add (name, mi);
					}/* else if (mi.Name.StartsWith ("Remove", StringComparison.Ordinal)) {
						name = mi.Name.Substring (6, mi.Name.Length - 6 - 7);
						rl.Add (name, mi);
					}*/
				}
				if (name != null && !nl.Contains (name))
					nl.Add (name);
			}

			foreach (var name in nl) {
				MethodInfo m;
				var g = gl.TryGetValue (name, out m) ? m : null;
				var s = sl.TryGetValue (name, out m) ? m : null;
				if (g != null || s != null)
					yield return SchemaContext.GetAttachableProperty (name, g, s);
				var a = al.TryGetValue (name, out m) ? m : null;
				//var r = rl.TryGetValue (name, out m) ? m : null;
				if (a != null)
					yield return SchemaContext.GetAttachableEvent (name, a);
			}
		}

		static readonly XamlMember [] empty_array = new XamlMember [0];

		protected virtual IEnumerable<XamlMember> LookupAllMembers ()
		{
			if (UnderlyingType == null)
				return BaseType != null ? BaseType.LookupAllMembers () : empty_array;
			if (all_members_cache == null) {
				all_members_cache = new List<XamlMember> (DoLookupAllMembers ());
				all_members_cache.Sort (TypeExtensionMethods.CompareMembers);
			}
			return all_members_cache;
		}

		IEnumerable<XamlMember> DoLookupAllMembers ()
		{
			// This is a hack that is likely required due to internal implementation difference in System.Uri. Our Uri has two readonly collection properties
			if (this == XamlLanguage.Uri)
				yield break;

			//var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

			foreach (var pi in UnderlyingType.GetRuntimeProperties()) {
				if (pi.GetPrivateGetMethod()?.IsStatic ?? pi.GetPrivateSetMethod()?.IsStatic ?? false)
					continue;
				if (pi.Name.Contains (".")) // exclude explicit interface implementations.
					continue;
				if (pi.CanRead && 
					(
						pi.CanWrite 
						|| IsCollectionType (pi.PropertyType)
						|| typeof (IXmlSerializable).GetTypeInfo().IsAssignableFrom (pi.PropertyType.GetTypeInfo())
					) 
					&& pi.GetIndexParameters ().Length == 0)
					yield return SchemaContext.GetProperty (pi);
			}
			foreach (var ei in UnderlyingType.GetRuntimeEvents())
				yield return SchemaContext.GetEvent (ei);
		}
		
		static bool IsPublicAccessor (MethodInfo mi)
		{
			return mi != null && mi.IsPublic;
		}

		bool IsCollectionType (Type type)
		{
			if (type == null)
				return false;
			var xt = SchemaContext.GetXamlType (type);
			return xt.LookupCollectionKind () != XamlCollectionKind.None;
		}

		protected virtual IList<XamlType> LookupAllowedContentTypes ()
		{
			// the actual implementation is very different from what is documented :(
			return null;

			/*
			var l = new List<XamlType> ();
			if (ContentWrappers != null)
				l.AddRange (ContentWrappers);
			if (ContentProperty != null)
				l.Add (ContentProperty.Type);
			if (ItemType != null)
				l.Add (ItemType);
			return l.Count > 0 ? l : null;
			*/
		}

		protected virtual XamlMember LookupAttachableMember (string name)
		{
			return LookupAllAttachableMembers ().FirstOrDefault (m => m.Name == name);
		}

		protected virtual XamlType LookupBaseType ()
		{
			if (base_type == null) {
				if (UnderlyingType == null)
					base_type = SchemaContext.GetXamlType (typeof (object));
				else
					base_type = type.GetTypeInfo().BaseType == null || type.GetTypeInfo().BaseType == typeof (object) ? null : SchemaContext.GetXamlType (type.GetTypeInfo().BaseType);
			}
			return base_type;
		}

		// This implementation is not verified. (No place to use.)
		protected internal virtual XamlCollectionKind LookupCollectionKind ()
		{
			if (collectionKind != null)
				return collectionKind.Value;


			if (UnderlyingType == null)
				collectionKind = BaseType != null ? BaseType.LookupCollectionKind () : XamlCollectionKind.None;
			else if (type.IsArray)
				collectionKind = XamlCollectionKind.Array;

			else if (type.ImplementsAnyInterfacesOf (typeof (IDictionary), typeof (IDictionary<,>)))
				collectionKind = XamlCollectionKind.Dictionary;

			else if (type.ImplementsAnyInterfacesOf (typeof (IList), typeof (ICollection<>)))
				collectionKind = XamlCollectionKind.Collection;
			else
				collectionKind = XamlCollectionKind.None;
			return collectionKind.Value;
		}

		protected virtual bool LookupConstructionRequiresArguments ()
		{
			if (UnderlyingType == null)
				return false;

			var typeInfo = UnderlyingType.GetTypeInfo ();

			if (typeInfo.IsValueType)
				return false;

			// not sure if it is required, but MemberDefinition return true while they are abstract and it makes no sense.
			if (typeInfo.IsAbstract)
				return true;

			// FIXME: probably some primitive types are treated as special.
			if (typeof(string).GetTypeInfo().IsAssignableFrom(typeInfo))
				return true;
			if (typeof(TimeSpan) == UnderlyingType)
				return false;


			return typeInfo.GetConstructors().Where(r => r.IsPublic).All(r => r.GetParameters().Length > 0);
		}

		protected virtual XamlMember LookupContentProperty ()
		{
			if (gotContentProperty)
				return contentProperty;
			gotContentProperty = true;

			var a = this.GetCustomAttribute<ContentPropertyAttribute> ();
			contentProperty = a != null && a.Name != null ? GetMember (a.Name) : null;
			return contentProperty;
		}

		protected virtual IList<XamlType> LookupContentWrappers ()
		{
			if (GetCustomAttributeProvider () == null)
				return null;

			var arr = GetCustomAttributeProvider ().GetCustomAttributes (typeof (ContentWrapperAttribute), false);
			if (arr == null || arr.Length == 0)
				return null;
			var l = new XamlType [arr.Length];
			for (int i = 0; i < l.Length; i++) 
				l [i] = SchemaContext.GetXamlType (((ContentWrapperAttribute) arr [i]).ContentWrapper);
			return l;
		}

		internal ICustomAttributeProvider GetCustomAttributeProvider ()
		{
			return LookupCustomAttributeProvider ();
		}

		protected internal virtual ICustomAttributeProvider LookupCustomAttributeProvider ()
		{
			return attributeProvider ?? (attributeProvider = UnderlyingType != null ? new TypeAttributeProvider(UnderlyingType) : null);
		}
		
		protected virtual XamlValueConverter<XamlDeferringLoader> LookupDeferringLoader ()
		{
			throw new NotImplementedException ();
		}

		protected virtual XamlTypeInvoker LookupInvoker ()
		{
			return invoker;
		}

		protected virtual bool LookupIsAmbient ()
		{
			return isAmbient ?? (isAmbient = this.GetCustomAttribute<AmbientAttribute> () != null).Value;
		}

		// It is documented as if it were to reflect spec. section 5.2,
		// but the actual behavior shows it is *totally* wrong.
		// Here I have implemented this based on the nunit test results. sigh.
		protected virtual bool LookupIsConstructible ()
		{
			if (UnderlyingType == null)
				return true;
			if (IsMarkupExtension)
				return true;
			if (UnderlyingType.GetTypeInfo().IsAbstract)
				return false;
			if (!IsNameValid)
				return false;
			return true;
		}

		protected virtual bool LookupIsMarkupExtension ()
		{
			return UnderlyingType != null && typeof (MarkupExtension).GetTypeInfo().IsAssignableFrom (UnderlyingType.GetTypeInfo());
		}

		protected virtual bool LookupIsNameScope ()
		{
			return UnderlyingType != null && typeof (INameScope).GetTypeInfo().IsAssignableFrom (UnderlyingType.GetTypeInfo());
		}

		protected virtual bool LookupIsNullable ()
		{
			return !type.GetTypeInfo().IsValueType || type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition () == typeof (Nullable<>);
		}

		protected virtual bool LookupIsPublic ()
		{
			return underlying_type == null || underlying_type.GetTypeInfo().IsPublic || underlying_type.GetTypeInfo().IsNestedPublic;
		}

		protected virtual bool LookupIsUnknown ()
		{
			return UnderlyingType == null;
		}

		protected virtual bool LookupIsWhitespaceSignificantCollection ()
		{
			// probably for unknown types, it should preserve whitespaces.
			return IsUnknown || this.GetCustomAttribute<WhitespaceSignificantCollectionAttribute> () != null;
		}

		protected virtual bool LookupIsXData ()
		{
			return CanAssignTo (SchemaContext.GetXamlType (typeof (IXmlSerializable)));
		}

		protected virtual XamlType LookupItemType ()
		{
			if (itemType != null)
				return itemType;

			var kind = LookupCollectionKind ();
			if (kind == XamlCollectionKind.Array)
				itemType = SchemaContext.GetXamlType(type.GetElementType());
			else if (kind == XamlCollectionKind.Dictionary) {
				if (!IsGeneric)
					itemType = SchemaContext.GetXamlType(typeof(object));
				else
					itemType = SchemaContext.GetXamlType(type.GetTypeInfo().GetGenericArguments()[1]);
			}
			else if (kind != XamlCollectionKind.Collection)
				return null;
            else if (!IsGeneric)
            {
                // support custom collections that inherit ICollection<T>
                var collectionType = type.GetTypeInfo().GetInterfaces().FirstOrDefault(r => r.GetTypeInfo().IsGenericType && r.GetGenericTypeDefinition() == typeof(ICollection<>));
                if (collectionType != null)
					itemType = SchemaContext.GetXamlType(collectionType.GetTypeInfo().GetGenericArguments()[0]);
				else
					itemType = SchemaContext.GetXamlType(typeof(object));
            }
			else
				itemType = SchemaContext.GetXamlType(type.GetTypeInfo().GetGenericArguments()[0]);
			return itemType;
		}

		protected virtual XamlType LookupKeyType ()
		{
			if (!IsDictionary)
				return null;
			if (!IsGeneric)
				return SchemaContext.GetXamlType(typeof (object));
			return SchemaContext.GetXamlType (type.GetTypeInfo().GetGenericArguments() [0]);
		}

		protected virtual XamlType LookupMarkupExtensionReturnType ()
		{
			var a = this.GetCustomAttribute<MarkupExtensionReturnTypeAttribute> ();
			return a != null ? SchemaContext.GetXamlType (a.ReturnType) : null;
		}

		protected virtual XamlMember LookupMember (string name, bool skipReadOnlyCheck)
		{
			// FIXME: verify if this does not filter out events.
			return LookupAllMembers ().FirstOrDefault (m => m.Name == name && (skipReadOnlyCheck || !m.IsReadOnly || m.Type.IsCollection || m.Type.IsDictionary || m.Type.IsArray));
		}

		protected virtual IList<XamlType> LookupPositionalParameters (int parameterCount)
		{
			if (UnderlyingType == null/* || !IsMarkupExtension*/) // see nunit tests...
				return null;

			// check if there is applicable ConstructorArgumentAttribute.
			// If there is, then return its type.
			if (parameterCount == 1) {
				foreach (var xm in LookupAllMembers ()) {
					var ca = xm.GetCustomAttributeProvider ().GetCustomAttribute<ConstructorArgumentAttribute> (false);
					if (ca != null)
						return new XamlType [] {xm.Type};
				}
			}

			var methods = (from m in UnderlyingType.GetTypeInfo().GetConstructors() where m.GetParameters ().Length == parameterCount select m).ToArray ();
			if (methods.Length == 1)
				return (from p in methods [0].GetParameters () select SchemaContext.GetXamlType (p.ParameterType)).ToArray ();

			if (SchemaContext.SupportMarkupExtensionsWithDuplicateArity)
				throw new NotSupportedException ("The default LookupPositionalParameters implementation does not allow duplicate arity of markup extensions");
			return null;
		}

		//static readonly BindingFlags flags_get_static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

		protected virtual EventHandler<XamlSetMarkupExtensionEventArgs> LookupSetMarkupExtensionHandler ()
		{
			var a = this.GetCustomAttribute<XamlSetMarkupExtensionAttribute> ();
			if (a == null)
				return null;
			var mi = type.GetRuntimeMethods().FirstOrDefault(r => r.Name == a.XamlSetMarkupExtensionHandler && r.IsStatic);
			if (mi == null)
				throw new ArgumentException ("Binding to XamlSetMarkupExtensionHandler failed");
			return (EventHandler<XamlSetMarkupExtensionEventArgs>) mi.CreateDelegate(typeof (EventHandler<XamlSetMarkupExtensionEventArgs>));
		}

		protected virtual EventHandler<XamlSetTypeConverterEventArgs> LookupSetTypeConverterHandler ()
		{
			var a = this.GetCustomAttribute<XamlSetTypeConverterAttribute> ();
			if (a == null)
				return null;
			var mi = type.GetRuntimeMethods().FirstOrDefault(r => r.Name == a.XamlSetTypeConverterHandler && r.IsStatic);
			if (mi == null)
				throw new ArgumentException ("Binding to XamlSetTypeConverterHandler failed");
			return (EventHandler<XamlSetTypeConverterEventArgs>) mi.CreateDelegate (typeof (EventHandler<XamlSetTypeConverterEventArgs>));
		}

		protected virtual bool LookupTrimSurroundingWhitespace ()
		{
			return this.GetCustomAttribute<TrimSurroundingWhitespaceAttribute> () != null;
		}

		protected virtual XamlValueConverter<TypeConverter> LookupTypeConverter ()
		{
			if (gotTypeConverter)
				return cachedTypeConverter;

			gotTypeConverter = true;

			var t = UnderlyingType;
			if (t == null)
				return null;

			// equivalent to TypeExtension.
			// FIXME: not sure if it should be specially handled here.
			if (t == typeof (Type))
				t = typeof (TypeExtension);

			var a = GetCustomAttributeProvider ();
			var ca = a?.GetCustomAttribute<TypeConverterAttribute>(false);
			if (ca != null)
				return cachedTypeConverter = SchemaContext.GetValueConverter<TypeConverter> (Type.GetType (ca.ConverterTypeName), this);

			if (t == typeof (object)) // This is a special case. ConverterType is null.
				return cachedTypeConverter = SchemaContext.GetValueConverter<TypeConverter> (null, this);

			// It's still not decent to check CollectionConverter.
			var tct = t.GetTypeConverter ()?.GetType ();
			if (tct != null && tct != typeof (TypeConverter)) //*PCL && tct != typeof (CollectionConverter)) //*PCL && tct != typeof (ReferenceConverter))
				return cachedTypeConverter = SchemaContext.GetValueConverter<TypeConverter> (tct, this);
			return null;
		}

		protected virtual Type LookupUnderlyingType ()
		{
			return underlying_type;
		}

		protected virtual bool LookupUsableDuringInitialization ()
		{
			var a = this.GetCustomAttribute<UsableDuringInitializationAttribute> ();
			return a != null && a.Usable;
		}

		static XamlValueConverter<ValueSerializer> string_value_serializer;

		protected virtual XamlValueConverter<ValueSerializer> LookupValueSerializer ()
		{
			return LookupValueSerializer (this, GetCustomAttributeProvider ());
		}

		internal static XamlValueConverter<ValueSerializer> LookupValueSerializer (XamlType targetType, ICustomAttributeProvider provider)
		{
			if (provider == null)
				return null;

			var a = provider.GetCustomAttribute<ValueSerializerAttribute> (true);
			if (a != null)
				return new XamlValueConverter<ValueSerializer> (a.ValueSerializerType ?? Type.GetType (a.ValueSerializerTypeName), targetType);

			if (targetType.BaseType != null) {
				var ret = targetType.BaseType.LookupValueSerializer ();
				if (ret != null)
					return ret;
			}

			if (targetType.UnderlyingType == typeof (string)) {
				if (string_value_serializer == null)
					string_value_serializer = new XamlValueConverter<ValueSerializer> (typeof (StringValueSerializer), targetType);
				return string_value_serializer;
			}

			return null;
		}

		static string GetXamlName (Type type)
		{
			string n;
			if (!type.GetTypeInfo().IsNestedPublic && !type.GetTypeInfo().IsNestedAssembly && !type.GetTypeInfo().IsNestedPrivate)
				n = type.Name;
			else
				n = GetXamlName (type.DeclaringType) + "+" + type.Name;
			if (type.GetTypeInfo().IsGenericType && !type.GetTypeInfo().ContainsGenericParameters) // the latter condition is to filter out "nested non-generic type within generic type".
				return n.Substring (0, n.IndexOf ('`'));
			else
				return n;
		}

		XamlMember[] constructorArguments;
		internal IEnumerable<XamlMember> GetConstructorArguments ()
		{
			if (constructorArguments != null)
				return constructorArguments;
			
			constructorArguments = GetAllMembers ()
				.Where (m => 
					m.UnderlyingMember != null 
					&& m.GetCustomAttributeProvider ().GetCustomAttribute<ConstructorArgumentAttribute> (false) != null
				)
				.ToArray ();
			return constructorArguments;
		}
	}
}
