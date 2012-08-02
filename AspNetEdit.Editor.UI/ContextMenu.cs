// 
// ContextMenu.cs
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

using Gtk;

using AspNetEdit.Editor.ComponentModel;

namespace AspNetEdit.Editor.UI
{
	public class ContextMenu
	{
		DesignerHost host;
		Menu contextMenu;

		public ContextMenu (DesignerHost hst)
		{
			host = hst;
			contextMenu = new Menu ();
		}

		public void Initialize ()
		{
			MenuItem deleteItem = new MenuItem ("Remove");
			deleteItem.Activated += new EventHandler (deleteItem_OnActivated);
			deleteItem.Show ();
			contextMenu.Add (deleteItem);
			MenuItem propertiesItem = new MenuItem ("Properties");
			propertiesItem.Activated += new EventHandler (propertiesItem_OnActivated);
			propertiesItem.Show ();
			contextMenu.Add (propertiesItem);
		}

		public void ShowMenu ()
		{
			contextMenu.Popup (null, null, null, 2, Gtk.Global.CurrentEventTime);
		}

		void deleteItem_OnActivated (object o, EventArgs args)
		{
			host.AspNetSerializer.RemoveSelected ();
		}

		void propertiesItem_OnActivated (object o, EventArgs args)
		{
			// show the property grid
		}
	}
}

