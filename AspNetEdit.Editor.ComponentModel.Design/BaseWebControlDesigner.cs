// 
// BaseWebControlDesigner.cs
//  
// Author:
//       Petar Dodev <petar.dodev@gmail.com>
// 
// Copyright (c) 2012 
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
using System.ComponentModel;
using System.ComponentModel.Design;

namespace AspNetEdit.Editor.ComponentModel.Design
{
	public class BaseWebControlDesigner : IDesigner
	{
		protected IComponent component;

		public BaseWebControlDesigner ()
		{
		}

		#region IDisposable implementation
		public virtual void Dispose ()
		{
		}
		#endregion

		#region IDesigner implementation
		public void DoDefaultAction ()
		{
		}

		public void Initialize (IComponent component)
		{
			this.component = component;
		}

		public IComponent Component {
			get {
				return component;
			}
		}

		public virtual DesignerVerbCollection Verbs {
			get {
				return new DesignerVerbCollection ();
			}
		}
		#endregion

		public virtual string GetDesignTimeHtml ()
		{
			string innerHtml = "AspNetControl";

			if (!string.IsNullOrEmpty (component.Site.Name))
				innerHtml = component.Site.Name;

			return "<span style=\"width:100px; height:20px; background-color: #e3e3e3; color: #670023;\">" + innerHtml + "</span>";
		}
	}
}

