/*
	handlers.js
	
      Copyright 2012 Petar Dodev

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
function control_onMouseDown (eventArgs) {
	var controlId = selMan.ExtractControlId (this.id);
	switch (eventArgs.which) {
		case 3:
		signalMan.ShowContextMenu (eventArgs.clientX, eventArgs.clientY, controlId);
		return false;
		break;
	}

	
	var changed = false;
	if (jQuery (this).hasClass (noConflict.prefix+"selected")) {
		if (selMan.Count () > 1) {
			if (eventArgs.ctrlKey) {
				selMan.Deselect (controlId);
			} else {
				selMan.Flush ();			
				selMan.Select (controlId);
			}
			changed = true;
		} else {
			
		}
	} else {
		if (selMan.Count () > 0) {
			if (eventArgs.ctrlKey) {
				selMan.Select (controlId)
			} else {
				selMan.Flush ();			
				selMan.Select (controlId);
			}
		} else {
			selMan.Select (controlId);
		}
		changed = true;
	}
	if (changed)
		signalMan.ChangeSelection (selMan.GetSelectedIds (), selMan.GetPrimaryIndex ());
	return false;
}