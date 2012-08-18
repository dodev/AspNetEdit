/*
* DesignSurfaceMessageManager.cs
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
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Collections.Generic;
using System.Threading;

using AspNetEdit.Editor.ComponentModel;
using AspNetEdit.Editor.UI;

namespace AspNetEdit.Editor.DesignerLink
{
	public class DesignerMessageManager
	{
		DesignerHost host;
		RootDesignerView view;

		public DesignerMessageManager (DesignerHost dhost, RootDesignerView rview)
		{
			host = dhost;
			view = rview;
		}

		private T DeserializeMessage<T> (string json)
		{
			T msg;
			using (MemoryStream stream = new MemoryStream (UnicodeEncoding.Default.GetBytes (json))) {
				DataContractJsonSerializer ds = new DataContractJsonSerializer (typeof (T));
				msg = (T)ds.ReadObject (stream);
			}
			return msg;
		}

		public void HandleMessage (string json)
		{
			// a message is an object, so always starts with a "{\"Message\":"
			string msgHeader = DesignerNames.MessagePreambula;
			if ((json.Length < msgHeader.Length) || (json.Substring (0, msgHeader.Length) != msgHeader))
				return;

			BasicMessage msg = DeserializeMessage<BasicMessage> (json);
			if (msg.MsgName == DesignerNames.MsgNameSelection) {
				ChangeSelection (msg.Arguments);
			} else if (msg.MsgName == DesignerNames.MsgNameContext) {
				ShowContextMenu (msg.Arguments);
			} else {

			}
		}

		void ShowContextMenu (string arguments)
		{
			//ContextMenuArgs args = DeserializeMessage<ContextMenuArgs> (arguments);
			view.CtxMenu.ShowMenu ();
		}

		private void ChangeSelection (string arguments)
		{
			var selServ = this.host.GetService (typeof (ISelectionService)) as ISelectionService;
			if (selServ == null)
				throw new Exception ("Could not get selection from designer host");

			SelectionChangedArguments args = DeserializeMessage<SelectionChangedArguments> (arguments);
			List<IComponent> components = new List<IComponent> ();
			foreach (string id in args.SelectedIds) {
				IComponent comp = host.GetComponent (id);
				if (comp != null)
					components.Add (comp);
			}
			selServ.SetSelectedComponents (components);
		}

		private class BasicMessage
		{
			public string MsgName { get; set; }
			public string Arguments { get; set; }
		}

		private class SelectionChangedArguments
		{
			// index of the primary selection elementt
			public int PrimarySelection { get; set; }
			public string[] SelectedIds { get; set; }
		}

		private class ContextMenuArgs
		{
			public int X { get; set; }
			public int Y { get; set; }
			public string ComponentId { get; set; }
		}
	}
}

