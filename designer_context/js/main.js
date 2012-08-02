jQuery (function () {
	// override the default action onclick for * elements
	// or following a link outside the designer
	jQuery ("*").click (function () {return false;});
	// disabling the selection of text with the mouse
	document.onmousedown = function() {return false;};
	// do not show the WebView's context menu
	document.oncontextmenu = function() {return false;};
	// initialize the selection manager
	selMan.Initialize ();	
});
