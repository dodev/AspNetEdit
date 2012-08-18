/*
* ContextMenu.cs
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
using System.Threading;

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
			host.RemoveSelectedControls ();
		}

		void propertiesItem_OnActivated (object o, EventArgs args)
		{
			Gtk.Application.Invoke (delegate {
				MonoDevelop.Ide.IdeApp.Workbench.GetPad<MonoDevelop.DesignerSupport.PropertyPad> ().BringToFront (true);
			});
		}
	}
}

