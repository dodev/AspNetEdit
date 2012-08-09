// 
// UndoTracker.cs
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
namespace AspNetEdit.Tools
{
	public class UndoTracker
	{
		int undoQueue;
		int redoQueue;

		public UndoTracker ()
		{
			undoQueue = 0;
			redoQueue = 0;
		}

		public UndoTracker (int undo, int redo)
		{
			undoQueue = undo;
			redoQueue = redo;
		}

		public void FinishAction ()
		{
			redoQueue = 0;
			undoQueue++;
		}

		public void UndoAction ()
		{
			if (undoQueue > 0) {
				undoQueue--;
				redoQueue++;
			}
		}
		
		public void RedoAction ()
		{
			if (redoQueue > 0) {
				redoQueue--;
				undoQueue++;
			}
		}

		public bool CanUndo
		{
			get {
				return undoQueue > 0;
			}
		}

		public bool CanRedo
		{
			get {
				return redoQueue > 0;
			}
		}
	}
}

