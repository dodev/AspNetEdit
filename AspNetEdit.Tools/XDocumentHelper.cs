// 
// XDocumentHelper.cs
//  
// Author:
//       Petar Dodev <petar.dodev@gmail.com>
// 
// Copyright (c) 2012 Petar Dodev
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using MonoDevelop.Xml.StateEngine;

namespace AspNetEdit.Tools
{
	public static class XDocumentHelper
	{
		public static string GetAttributeValueCI (XAttributeCollection attributes, string key)
		{
			XName nameKey = new XName (key.ToLowerInvariant ());

			foreach (XAttribute attr in attributes) {
				if (attr.Name.ToLower () == nameKey)
					return attr.Value;
			}
			return String.Empty;
		}

		public static XAttribute GetAttributeCI (XAttributeCollection attributes, string key)
		{
			XName nameKey = new XName (key.ToLowerInvariant ());

			foreach (XAttribute attr in attributes) {
				if (attr.Name.ToLower () == nameKey)
					return attr;
			}
			return null;
		}

		public static bool IsRunAtServer (XElement el)
		{
			XName runat = new XName ("runat");
			foreach (XAttribute a  in el.Attributes) {
				if ((a.Name.ToLower () == runat) && (a.Value.ToLower () == "server"))
					return true;
			}
			return false;
		}
	}
}

