/* 
* RootDesignerView.cs - The Gecko# design surface returned by the WebForms Root Designer.
*
* Authors: 
*  Michael Hutchinson <m.j.hutchinson@gmail.com>
*  Petar Dodev <petar.dodev@gmail.com>
*  
* Copyright (C) 2005 Michael Hutchinson
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
using System.ComponentModel.Design;
using System.ComponentModel;
using System.Text;
using System.Web.UI;
using System.Collections;
using System.IO;

using AspNetEdit.Editor.ComponentModel;
using AspNetEdit.Editor.DesignerLink;

using Gtk;
using WebKit;

namespace AspNetEdit.Editor.UI
{
	public class RootDesignerView : WebView
	{
		DesignerHost host;
		bool active = false;
		string baseUri;
		string designerContext;
		DesignerMessageManager msgManager;
		ContextMenu menu;
		
		// dodev: To be tested with WebKit
		//there's weird bug where a second Gecko instance *can't* be created
		//so until it's fixed we reuse share one instance
		//TODO: make it so we can have more than one shown at the same time
//		public static RootDesignerView instance = null;
//		
//		public static RootDesignerView GetInstance (IDesignerHost host)
//		{
//			if (instance == null)
//				instance = new RootDesignerView (host);
//			instance.active = false;
//			return instance;
//		}

		public RootDesignerView (IDesignerHost host)
			: base()
		{
			//we use the host to get services and designers
			this.host =  host as DesignerHost;
			if (this.host == null)
				throw new ArgumentNullException ("host");

			baseUri = String.Empty;
			designerContext = String.Empty;
			msgManager = new DesignerMessageManager (host as DesignerHost,  this);
			menu = new ContextMenu (host as DesignerHost);
			menu.Initialize ();

			// subscribe to messages from the designer surface
			this.TitleChanged += new TitleChangedHandler (WebView_OnTitleChanged);
		}

		public ContextMenu CtxMenu {
			get { return menu; }
		}

		public void InitProperties ()
		{
			// setting the baseUri to the project's root
			// it will enable the WebView to load user's css files and images with relative URIs
			string projectDir = System.IO.Path.GetDirectoryName (MonoDevelop.Ide.IdeApp.Workbench.ActiveDocument.Project.FileName.ToString ());
			baseUri = "file://" + projectDir + System.IO.Path.DirectorySeparatorChar.ToString ();

			// generating the designer context
			string scriptTag = "<script type=\"text/javascript\" src=\"{0}\"></script>";
			string cssLinkTag = "<link rel=\"stylesheet\" type=\"text/css\" href=\"{0}\" />";

			// the designer context is stored in the assembly's directory
			// TODO: Copy the designer_context dir into the assembly directory, when building the addin
			string designerDir = System.IO.Path.Combine (
				System.IO.Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly ().Location),
				"designer_context"
				);
			string scriptDir = System.IO.Path.Combine (designerDir, "js");
			string[] scripts = {
				"jquery-1.7.2.min.js",
				"config.js",
				"handlers.js",
				"SelectionManager.js",
				"SignalManager.js",
				"globals.js",
				"main.js"
			};
			string styleDir = System.IO.Path.Combine (designerDir, "css");
			string[] styleSheets = {
				"editor_style.css"
			};

			StringBuilder sb = new StringBuilder ();
			foreach (string script in scripts)
				sb.AppendLine (String.Format (scriptTag, "file://" + System.IO.Path.Combine (scriptDir, script)));
			foreach (string styleFile in styleSheets)
				sb.AppendLine (String.Format (cssLinkTag, "file://" + System.IO.Path.Combine (styleDir, styleFile)));
			sb.AppendLine ();

			designerContext = sb.ToString ();
		}

		public string DesignerContext {
			get { 
				return designerContext; 
			} 
		}

		#region WebView Communication
	
		public void LoadDocumentInDesigner (string htmlDocument)
		{
			this.LoadString (htmlDocument, null, null, baseUri);
		}

		public void LoadDocumentInDesigner (string htmlDocument, string encoding)
		{
			this.LoadString (htmlDocument, null, encoding, baseUri);
		}

		void WebView_OnTitleChanged (object o, WebKit.TitleChangedArgs args)
		{
			try {
				msgManager.HandleMessage (args.Title);
			} catch (Exception ex) {
				System.Diagnostics.Trace.WriteLine (ex.ToString ());
			}
		}

		#endregion
	}
}
