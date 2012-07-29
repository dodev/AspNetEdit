 /* 
 * RootDesignerView.cs - The Gecko# design surface returned by the WebForms Root Designer.
 * 			
 * Authors: 
 *  Michael Hutchinson <m.j.hutchinson@gmail.com>
 *  
 * Copyright (C) 2005 Michael Hutchinson
 *
 * This sourcecode is licenced under The MIT License:
 * 
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit
 * persons to whom the Software is furnished to do so, subject to the
 * following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
 * NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
 * OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
 * USE OR OTHER DEALINGS IN THE SOFTWARE.
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
		
		// dodev: To be tested with WebKit
		//there's weird bug where a second Gecko instance *can't* be created
		//so until it's fixed we reuse share one instance
		//TODO: make it so we can have more than one shown at the same time
		public static RootDesignerView instance = null;
		
		public static RootDesignerView GetInstance (IDesignerHost host)
		{
			if (instance == null)
				instance = new RootDesignerView (host);
			instance.active = false;
			return instance;
		}

		private RootDesignerView (IDesignerHost host)
			: base()
		{
			//we use the host to get services and designers
			this.host =  host as DesignerHost;
			if (this.host == null)
				throw new ArgumentNullException ("host");

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
				"control_style.css"
			};

			StringBuilder sb = new StringBuilder ();
			foreach (string script in scripts)
				sb.AppendLine (String.Format (scriptTag, "file://" + System.IO.Path.Combine (scriptDir, script)));
			foreach (string styleFile in styleSheets)
				sb.AppendLine (String.Format (cssLinkTag, "file://" + System.IO.Path.Combine (styleDir, styleFile)));
			sb.AppendLine ();

			designerContext = sb.ToString ();

			msgManager = new DesignerMessageManager (host as DesignerHost);
			this.TitleChanged += new TitleChangedHandler (WebView_OnTitleChanged);
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
