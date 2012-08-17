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
// handles a mousedown on a marker for a control
function control_onMouseDown (eventArgs) {
	
	// get the control's id from the marker's class attribute
	var controlId = selMan.ExtractControlId (this.id);
	
	// check which button is down
	// TODO: move all the code in cases
	switch (eventArgs.which) {
		
		// right click
		case 3:
		signalMan.ShowContextMenu (eventArgs.clientX, eventArgs.clientY, controlId);
		return false;
		break;
	}

	// altering the selected items list
	
	// a flag used to note if current combination of user interactions
	// changed the selection, and if should a message be send to sync
	// the backend
	var changed = false;
	// the clicked marker is marked as selected
	if (jQuery (this).hasClass (noConflict.prefix+"selected")) {
		// if we have more than one item in the list
		if (selMan.Count () > 1) {
			if (eventArgs.ctrlKey) {
				// ctrl pressed + there are other items in the selection
				// deselects the item
				selMan.Deselect (controlId);
			} else {
				// no ctrl + other items in the selected list
				// deselect all but the current item
				selMan.Flush ();			
				selMan.Select (controlId);
			}
			changed = true;
		} else {
			// perform no action as the user clicks on a selected item
			// which is the only one in the selected list
		}
	// the clicked marker was not "selected"
	} else {
		if (selMan.Count () > 0) {
			if (eventArgs.ctrlKey) {
				// ctrl pressed - add the item to the list
				selMan.Select (controlId)
			} else {
				// no ctrl - flush the selection list
				// add only the current item
				selMan.Flush ();			
				selMan.Select (controlId);
			}
		} else {
			// no items in the list - just add the current
			selMan.Select (controlId);
		}
		changed = true;
	}
	// send message to the backend if the selection list was changed
	if (changed)
		signalMan.ChangeSelection (selMan.GetSelectedIds (), selMan.GetPrimaryIndex ());
	return false;
}