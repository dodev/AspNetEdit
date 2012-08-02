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