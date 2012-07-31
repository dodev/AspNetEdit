//
// MonoDevelopProxy.cs: Proxies methods that run in the MD process so 
//    that they are remotely accessible to the AspNetEdit process.
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
using System.CodeDom;
using System.Collections.Generic;
using System.Reflection;

using MonoDevelop.Ide;
using MonoDevelop.Ide.TypeSystem;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.DesignerSupport;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;

namespace AspNetEdit.Integration
{
	
	public class MonoDevelopProxy : MarshalByRefObject, IDisposable
	{
		Project project;
		IType fullClass;
		IUnresolvedTypeDefinition nonDesignerClass;
		
		public MonoDevelopProxy (Project project, string className)
		{
			this.project = project;
			
			if (className != null) {
				ICompilation compilation = TypeSystemService.GetProjectContext (project).CreateCompilation ();
				foreach (ITypeDefinition itd in compilation.MainAssembly.TopLevelTypeDefinitions) {
					if (itd.FullName == className)
						fullClass = itd as IType;
				}
			}				
			else
				fullClass = null;
			
			if (fullClass != null)
				nonDesignerClass = MonoDevelop.DesignerSupport.CodeBehind.GetNonDesignerClass (fullClass);
			else
				nonDesignerClass = null;
			
		}
		
		//keep this object available through remoting
		public override object InitializeLifetimeService ()
		{
			return null;
		}
		
		bool disposed = false;
		public void Dispose ()
		{
			if (disposed)
				return;
			disposed = true;
			
			//The proxy uses InitializeLifetimeService to make sure it stays connected
			//So need to make sure we don't keep it around forever
			System.Runtime.Remoting.RemotingServices.Disconnect (this);
		}
		
		//TODO: make this work with inline code
		#region event binding

		public bool IdentifierExistsInCodeBehind (string trialIdentifier)
		{
			if (fullClass == null)
				return false;
			
			return BindingService.IdentifierExistsInClass (fullClass, trialIdentifier);
		}
		
		public string GenerateIdentifierUniqueInCodeBehind (string trialIdentifier)
		{
			if (fullClass == null)
				return trialIdentifier;
			
			return BindingService.GenerateIdentifierUniqueInClass (fullClass, trialIdentifier);
		}
		
		
		public string[] GetCompatibleMethodsInCodeBehind (MethodInfo methodInfo)
		{
			if (fullClass == null)
				return new string[0];

			ParameterInfo[] reflectionParams = methodInfo.GetParameters ();
			List<IMethod> compatMeth = new List<IMethod> ();
			IType[] pars = new IType[reflectionParams.Length];
			List<IType>[] baseTypes = new List<IType>[reflectionParams.Length];
			ICompilation compilation = TypeSystemService.GetCompilation (IdeApp.Workbench.ActiveDocument.Project);

			for (int i = 0; i < reflectionParams.Length; i++) {
				pars[i] = reflectionParams[i].ParameterType.ToTypeReference ().Resolve (compilation);
				baseTypes[i] = new List<IType> (pars[i].GetAllBaseTypes ());
			}

			var matchMethType = methodInfo.ReturnType.ToTypeReference ().Resolve (compilation);

			foreach (IMethod mmethod in fullClass.GetMethods (null, null,GetMemberOptions.IgnoreInheritedMembers)) {
				if (mmethod.IsPrivate || mmethod.Parameters.Count != pars.Length || mmethod.IsInternal)
					continue;

				if (mmethod.ReturnType.FullName != matchMethType.FullName)
					continue;

				bool allCompatible = true;
				
				//compare each parameter
				for (int i = 0; i < pars.Length; i++) {
					if (pars[i].FullName != mmethod.Parameters[i].Type.FullName) {
						allCompatible = false;
						break;
					}

					List<IType> insideBTypes = new List<IType> (mmethod.Parameters[i].Type.GetAllBaseTypes ());
					if (insideBTypes.Count != baseTypes[i].Count) {
						allCompatible = false;
						break;
					}
					for (int j = 0; j < baseTypes[i].Count; j++) {
						if (baseTypes[i][j].FullName != insideBTypes[j].FullName) {
							allCompatible = false;
							break;
						}
					}
				}
				
				if (allCompatible)
					compatMeth.Add (mmethod);
			}

			string[] names = new string[compatMeth.Count];
			for (int i = 0; i < names.Length; i++)
				names[i] = compatMeth[i].Name;
			return names;
		}
		
		public bool ShowMethod (CodeMemberMethod method)
		{
			if (nonDesignerClass == null)
				return false;
			
			Gtk.Application.Invoke (delegate {
				BindingService.CreateAndShowMember (project, fullClass.GetDefinition (), nonDesignerClass, method); 
			});
			
			return true;
		}
		
		public bool ShowLine (int lineNumber)
		{
			if (nonDesignerClass == null)
				return false;
			
			Gtk.Application.Invoke (delegate {
				IdeApp.Workbench.OpenDocument (
					new FilePath (nonDesignerClass.ParsedFile.FileName),
					lineNumber, 
					1,
					MonoDevelop.Ide.Gui.OpenDocumentOptions.Default);
			});
			
			return true;
		}

		#endregion event binding
	}
}
