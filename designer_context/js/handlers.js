jQuery (function () {
	// override the default action for elements that will cause reloading the page
	// or following a link outside the designer
	jQuery ("a, input[type=\"submit\"], button[type=\"submit\"]").click (function () {
		return false;
	});
	
	// handles the click event on control containers
	jQuery (classPrefix+"control_container").mousedown (function (eventArgs) {
		var controlId = ExtractIdFromContainer (this);
		if (jQuery (this).hasClass (prefix+"selected")) {
			if (selMan.Count () > 1) {
				if (eventArgs.ctrlKey) {
					jQuery (this).removeClass (prefix+"selected");
					selMan.RemoveItem (controlId);
				} else {
					DeselectAll ();					
					MarkCurrentItem (this, controlId);
				}
			} else {
				
			}
		} else {
			if (selMan.Count () > 0) {
				if (eventArgs.ctrlKey) {
					MarkCurrentItem (this, controlId);
				} else {
					DeselectAll ();					
					MarkCurrentItem (this, controlId);
				}
			} else {
				MarkCurrentItem (this, controlId);
			}
		}
		selMan.CommitChanges ();
		return false;
	});
	
	var MarkCurrentItem = function (tagObj, id) {
		jQuery (tagObj).addClass (prefix+"selected");
		selMan.AddItem (id);
	};
	
	var DeselectAll = function () {
		jQuery (classPrefix+"selected").each (function () {
			jQuery (this).removeClass (prefix+"selected");
		});
		selMan.Flush ();
	};
});