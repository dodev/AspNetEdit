/*
* XDocumentHelper.cs - useful methods for working with XDocument instances
* 					and AspNetParsedDocuments
* 
* Authors: 
*  Petar Dodev <petar.dodev@gmail.com>
*
* Copyright (C) 2012 Petar Dodev
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*	http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/
using System;
using MonoDevelop.Xml.StateEngine;

namespace AspNetEdit.Tools
{
	public static class XDocumentHelper
	{
		/// <summary>
		/// Gets the value of an attribute with name. The name is queried with a case insensitive search
		/// </summary>
		/// <returns>
		/// The value of the attribute or null if the an attribute with that name was not found
		/// </returns>
		/// <param name='attributes'>
		/// Attribute collection to be traversed
		/// </param>
		/// <param name='key'>
		/// Name of the attribute.
		/// </param>
		public static string GetAttributeValueCI (XAttributeCollection attributes, string key)
		{
			//XName nameKey = new XName (key.ToLowerInvariant ());

			foreach (XAttribute attr in attributes) {
				if (IsXNameEqualCI (attr.Name, key))
					return attr.Value;
			}
			return String.Empty;
		}

		/// <summary>
		/// Gets the XAttribute instance of an attribute. The name is queried with a case insensitive search
		/// </summary>
		/// <returns>
		/// The XAttribute instance of an attribute or null, if none was found.
		/// </returns>
		/// <param name='attributes'>
		/// Attribute collection.
		/// </param>
		/// <param name='key'>
		/// Name of the attribute.
		/// </param>
		public static XAttribute GetAttributeCI (XAttributeCollection attributes, string key)
		{
			//XName nameKey = new XName (key.ToLowerInvariant ());

			foreach (XAttribute attr in attributes) {
				if (IsXNameEqualCI (attr.Name, key))
					return attr;
			}
			return null;
		}

		/// <summary>
		/// Determines whether this XElement instance contains a runat="server" attribute.
		/// </summary>
		/// <returns>
		/// <c>true</c> if this instance contains a runat="server" attribute; otherwise, <c>false</c>.
		/// </returns>
		/// <param name='el'>
		/// The XElement instace to be checked
		/// </param>
		public static bool IsRunAtServer (XElement el)
		{
			//XName runat = new XName ("runat");
			foreach (XAttribute a  in el.Attributes) {
				if (IsXNameEqualCI (a.Name, "runat") && (a.Value.ToLower () == "server"))
					return true;
			}
			return false;
		}

		public static bool IsXNameEqualCI (XName el, string name)
		{
			return (bool) (el.Name.ToLowerInvariant () == name.ToLowerInvariant ());
		}
	}
}

