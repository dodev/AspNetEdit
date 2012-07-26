jQuery (function () {
	// override the default action onclick for * elements
	// or following a link outside the designer
	jQuery ("*").click (function () {
		return false;
	});
	
	selMan.Initialize ();
	
});
