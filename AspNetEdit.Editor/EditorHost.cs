/*
* EditorHost.cs: Host for AspNetEdit designer.
* 
* Authors: 
*  Michael Hutchinson <m.j.hutchinson@gmail.com>
*  Petar Dodev <petar.dodev@gmail.com>
*
* Copyright (C) 2006 Michael Hutchinson
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
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing.Design;
using System.ComponentModel.Design.Serialization;

using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using MonoDevelop.DesignerSupport.Toolbox;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.SourceEditor;

using AspNetEdit.Editor.ComponentModel;
using AspNetEdit.Editor.UI;
using AspNetEdit.Integration;

namespace AspNetEdit.Editor
{
	
	public class EditorHost : GuiSyncObject, IDisposable
	{
		DesignerHost designerHost;
		ServiceContainer services;
		RootDesignerView designerView;
		MonoDevelopProxy proxy;
		
		public EditorHost (MonoDevelopProxy proxy)
		{
			this.proxy = proxy;
			
			//set up the services
			services = new ServiceContainer ();
			services.AddService (typeof(INameCreationService), new NameCreationService ());
			services.AddService (typeof(ISelectionService), new SelectionService ());
			services.AddService (typeof(ITypeResolutionService), new TypeResolutionService ());
			services.AddService (
				typeof(IEventBindingService),
				new AspNetEdit.Editor.ComponentModel.EventBindingService (proxy)
			);
			ExtenderListService extListServ = new ExtenderListService ();
			services.AddService (typeof(IExtenderListService), extListServ);
			services.AddService (typeof(IExtenderProviderService), extListServ);
			services.AddService (typeof(ITypeDescriptorFilterService), new TypeDescriptorFilterService ());
			services.AddService (typeof (IMenuCommandService), new AspNetEdit.Editor.ComponentModel.MenuCommandService ());
			//services.AddService (typeof (IToolboxService), toolboxService);

			var project = MonoDevelop.Ide.IdeApp.Workbench.ActiveDocument.Project as AspNetAppProject;
			var aspParsedDoc = MonoDevelop.Ide.IdeApp.Workbench.ActiveDocument.ParsedDocument as AspNetParsedDocument;
			if (project != null && aspParsedDoc != null) {
				WebFormReferenceManager refMan = new WebFormReferenceManager (project);
				refMan.Doc = aspParsedDoc;
				services.AddService (typeof(WebFormReferenceManager), refMan);
			}

			System.Diagnostics.Trace.WriteLine ("Creating DesignerHost");
			designerHost = new DesignerHost (services, this);
			System.Diagnostics.Trace.WriteLine ("Created DesignerHost");
			designerHost.DocumentChanged += new DesignerHost.DocumentChangedEventHandler (OnDocumentChanged);
		}
		
		public void Initialise ()
		{
			DispatchService.AssertGuiThread ();
			System.Diagnostics.Trace.WriteLine ("Loading document into DesignerHost");
			designerHost.LoadDocument ();
			System.Diagnostics.Trace.WriteLine ("Loaded document into DesignerHost");
			
			designerHost.Activate ();
			System.Diagnostics.Trace.WriteLine ("DesignerHost activated; getting designer view");
			
			IRootDesigner rootDesigner = (IRootDesigner)designerHost.GetDesigner (designerHost.RootComponent);
			designerView = (RootDesignerView)rootDesigner.GetView (ViewTechnology.Default);
//			designerView.Realized += delegate {
//				System.Diagnostics.Trace.WriteLine ("Designer view realized");
//			};
			designerView.Realized += new EventHandler (designerHost.RootDesignerView_Realized);
		}
		
		public Gtk.Widget DesignerView {
			get {
				if (designerView == null)
					throw new InvalidOperationException ("DesignerView has not been initialised. Have you sucessfully called EditorHost.Initialise?");
				return designerView;
			}
		}
		
		public ServiceContainer Services {
			get { return services; }
		}
		
		public DesignerHost DesignerHost {
			get { return designerHost; }
		}
		
		public void UseToolboxNode (ItemToolboxNode node)
		{
			//invoke in GUI thread as it catches and displays exceptions nicely
			Gtk.Application.Invoke ( delegate { handleToolboxNode (node); }); 
		}
		
		private void handleToolboxNode (ItemToolboxNode node)
		{
			ToolboxItemToolboxNode tiNode = node as ToolboxItemToolboxNode;
				
			if (tiNode != null) {
				//load the type into this process and get the ToolboxItem 
				tiNode.Type.Load ();
				System.Drawing.Design.ToolboxItem ti = tiNode.GetToolboxItem ();
				
				//web controls have sample HTML that need to be deserialised, in a ToolboxDataAttribute
				//TODO: Fix WebControlToolboxItem and (mono classlib's use of it) so we don't have to mess around with type lookups and attributes here
				if (ti.AssemblyName != null && ti.TypeName != null) {
					//look up and register the type
					ITypeResolutionService typeRes = (ITypeResolutionService)designerHost.GetService (typeof(ITypeResolutionService));					
					typeRes.ReferenceAssembly (ti.AssemblyName);
					Type controlType = typeRes.GetType (ti.TypeName, true);
					
					//read the WebControlToolboxItem data from the attribute
					AttributeCollection atts = TypeDescriptor.GetAttributes (controlType);
					
					System.Web.UI.ToolboxDataAttribute tda = (System.Web.UI.ToolboxDataAttribute)atts [typeof(System.Web.UI.ToolboxDataAttribute)];
						
					//if it's present
					if (tda != null && tda.Data.Length > 0) {
						//look up the tag's prefix and insert it into the data						
						WebFormReferenceManager webRef = designerHost.GetService (typeof(WebFormReferenceManager)) as WebFormReferenceManager;
						if (webRef == null)
							throw new Exception("Host does not provide an IWebFormReferenceManager");
						string aspText = String.Format (tda.Data, webRef.GetTagPrefix (controlType));
						System.Diagnostics.Trace.WriteLine ("Toolbox processing ASP.NET item data: " + aspText);
							
						//and add it to the document
						designerHost.RootDocument.InsertFragment (aspText);
						return;
					}
				}
				
				//No ToolboxDataAttribute? Get the ToolboxItem to create the components itself
				ti.CreateComponents (designerHost);
			}
		}
		
//		public void LoadDocument (string document, string fileName)
//		{
//			System.Diagnostics.Trace.WriteLine ("Copying document to editor.");
//			
//			//invoke in GUI thread as it catches and displays exceptions nicely
//			Gtk.Application.Invoke ( delegate {
//				designerHost.Reset ();
//				designerHost.Load (document, fileName);
//				designerHost.Activate ();
//			});
//		}
		
		public string GetDocument ()
		{
			DispatchService.AssertGuiThread ();
			string doc = "";
			
			System.Diagnostics.Trace.WriteLine ("Persisting document.");
			//doc = designerHost.PersistDocument ();
			//doc = designerHost.GetEditableAspNetCode ();
				
			return doc;
		}

		public void OnDocumentChanged (DesignerHost.DocumentChangedEventArgs ea)
		{
			if ((designerView != null) && designerView.IsRealized)
				Gtk.Application.Invoke ( delegate {
					designerView.LoadDocumentInDesigner (ea.Html);
				});
		}
		
		#region IDisposable
		
		bool disposed = false;
		public virtual void Dispose ()
		{
			System.Diagnostics.Trace.WriteLine ("Disposing editor host.");
			
			if (disposed)
				return;
			disposed = true;
			
			if (designerView == null) {
				System.Diagnostics.Trace.WriteLine ("DesignerView is already null when disposing; was it created correctly?");
			} else {
				designerView.Dispose ();
			}
			
			GC.SuppressFinalize (this);
		}
		
		~EditorHost ()
		{
			Dispose ();
		}
		
		#endregion IDisposable
	}
}
