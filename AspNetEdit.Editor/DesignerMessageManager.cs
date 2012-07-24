// 
// DesignSurfaceMessageManager.cs
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
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Collections.Generic;

using AspNetEdit.Editor.ComponentModel;

namespace AspNetEdit.Editor
{
	public class DesignerMessageManager
	{
		DesignerHost host;

		public DesignerMessageManager (DesignerHost dhost)
		{
			host = dhost;
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
			string msgHeader = "{\"MsgName\":";
			if ((json.Length < msgHeader.Length) || (json.Substring (0, msgHeader.Length) != msgHeader))
				return;

			BasicMessage msg = DeserializeMessage<BasicMessage> (json);
			switch (msg.MsgName) {
			case "selection_changed":
				ChangeSelection (msg.Arguments);
				break;
			default:
				break;
			}
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
	}
}

