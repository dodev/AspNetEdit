// 
// WebTypeDescriptor.cs
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
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Web.UI.WebControls;
using AspNetEdit.Editor.ComponentModel.Design;

namespace AspNetEdit.Editor.ComponentModel
{
	public static class WebTypeDescriptor
	{
		public static IDesigner GetDesigner (IComponent component)
		{
			Type designer = null;
			Type compType = component.GetType ();
			if (Designers.ContainsKey (compType))
				designer = Designers[compType];
			else
				designer = typeof (BaseWebControlDesigner);

			return (IDesigner) Activator.CreateInstance (designer);
		}

		static Dictionary<Type, Type> Designers = new Dictionary<Type, Type> () {
			{typeof (Button), typeof (ButtonDesigner)},
			{typeof (TextBox), typeof (TextBoxDesigner)},
			{typeof (Label), typeof (LabelDesigner)}
		};
	}
}

