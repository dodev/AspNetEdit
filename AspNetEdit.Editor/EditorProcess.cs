//
// EditorProcess.cs: Hosts AspNetEdit in a remote process for MonoDevelop.
//
// Authors:
//   Michael Hutchinson <m.j.hutchinson@gmail.com>
//
// Copyright (C) 2006 Michael Hutchinson
//
//
// This source code is licenced under The MIT License:
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using Gtk;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing.Design;
using System.ComponentModel.Design.Serialization;
using System.IO;

using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.Core.Execution;
using MonoDevelop.Core;
using MonoDevelop.DesignerSupport;
using MonoDevelop.DesignerSupport.Toolbox;
using MonoDevelop.SourceEditor;

using AspNetEdit.Editor.UI;
using AspNetEdit.Editor.ComponentModel;
using AspNetEdit.Integration;
using AspNetEdit.Editor;

namespace AspNetEdit.Editor
{
	[AddinDependency ("MonoDevelop.AspNetEdit")]
	public class EditorProcess //: RemoteDesignerProcess
	{
		EditorHost host;
		ScrolledWindow webKitFrame;
		Frame designerFrame;
		//VBox outerBox;
//		PropertyGrid propertyGrid;
		
		public EditorProcess () //: base ()
		{
			#if TRACE
				System.Diagnostics.TextWriterTraceListener listener = new System.Diagnostics.TextWriterTraceListener (System.Console.Out);
				System.Diagnostics.Trace.Listeners.Add (listener);
			#endif
		}
		
		public void Initialise (MonoDevelopProxy proxy, Frame designerFrame)
		{
			System.Diagnostics.Trace.WriteLine ("Creating AspNetEdit EditorHost");
			host = new EditorHost (proxy);
			host.Initialise ();
			System.Diagnostics.Trace.WriteLine ("Created AspNetEdit EditorHost");
			
			//StartGuiThread ();
			Gtk.Application.Invoke ( delegate { LoadGui (designerFrame); });
		}
		
		public EditorHost Editor {
			get { return host; }
		}
		
//		protected override void HandleError (Exception e)
//		{
//			//remove the grid in case it was the source of the exception, as GTK# expose exceptions can fire repeatedly
//			//also user should not be able to edit things when showing exceptions
////			if (propertyGrid != null) {
////				Gtk.Container parent = propertyGrid.Parent as Gtk.Container;
////				if (parent != null)
////					parent.Remove (propertyGrid);
////				
////				propertyGrid.Destroy ();
////				propertyGrid = null;
////			}
//			
//			//show the error message
//			//base.HandleError (e);
//		}
		
		void LoadGui (Frame desFrame)
		{
			designerFrame = desFrame;
			System.Diagnostics.Trace.WriteLine ("Building AspNetEdit GUI");
			//outerBox = new Gtk.VBox ();

			webKitFrame = new ScrolledWindow ();
			webKitFrame.BorderWidth = 1;
			webKitFrame.Add (host.DesignerView);
			//outerBox.PackEnd (webKitFrame, true, true, 0);
			
			//Toolbar tb = BuildToolbar ();
			//outerBox.PackStart (tb, false, false, 0);
			
			//outerBox.ShowAll ();
			webKitFrame.ShowAll ();
			designerFrame.Add (webKitFrame);
			//base.DesignerWidget = outerBox;
			
			//grid picks up some services from the designer host
//			propertyGrid = new PropertyGrid (host.Services);
//			propertyGrid.ShowAll ();
//			base.PropertyGridWidget = propertyGrid;
			System.Diagnostics.Trace.WriteLine ("Built AspNetEdit GUI");
		}
		
		Toolbar BuildToolbar ()
		{
			Toolbar buttons = new Toolbar ();
			
			// * Clipboard
			
			ToolButton undoButton = new ToolButton (Stock.Undo);
			buttons.Add (undoButton);
			//undoButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Undo); };

			ToolButton redoButton = new ToolButton (Stock.Redo);
			buttons.Add (redoButton);
			//redoButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Redo); };

			ToolButton cutButton = new ToolButton (Stock.Cut);
			buttons.Add (cutButton);
			//cutButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Cut); };

			ToolButton copyButton = new ToolButton (Stock.Copy);
			buttons.Add (copyButton);
			//copyButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Copy); };

			ToolButton pasteButton = new ToolButton (Stock.Paste);
			buttons.Add (pasteButton);
			//pasteButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Paste); };
			
			
			// * Text style
			
			buttons.Add (new SeparatorToolItem());
			
			ToolButton boldButton = new ToolButton (Stock.Bold);
			buttons.Add (boldButton);
			//boldButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Bold); };
			
			ToolButton italicButton = new ToolButton (Stock.Italic);
			buttons.Add (italicButton);
			//italicButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Italic); };
			
			ToolButton underlineButton = new ToolButton (Stock.Underline);
			buttons.Add (underlineButton);
			//underlineButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Underline); };
			
			ToolButton indentButton = new ToolButton (Stock.Indent);
			buttons.Add (indentButton);
			//indentButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Indent); };
			
			ToolButton unindentButton = new ToolButton (Stock.Unindent);
			buttons.Add (unindentButton);
			//unindentButton.Clicked += delegate { host.DesignerHost.RootDocument.DoCommand (EditorCommand.Outdent); };
			
			return buttons;
		}
		
		bool disposed = false;
		public void Dispose ()
		{
			System.Diagnostics.Trace.WriteLine ("Disposing AspNetEdit editor process");
			
			if (disposed)
				return;
			disposed = true;

			designerFrame.Remove (webKitFrame);
			webKitFrame.Dispose ();
			//outerBox.Dispose ();

			host.Dispose ();		
			//base.Dispose ();
			System.Diagnostics.Trace.WriteLine ("AspNetEdit editor process disposed");
		}
	}
}
