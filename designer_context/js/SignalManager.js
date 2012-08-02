function SignalManager () {
	this.Send = function (msg) {
		jQuery ("title").html (JSON.stringify (msg));
	};
	
	this.ChangeSelection = function (idArr, primary) {
		var args = {
			"SelectedIds" : idArr,
			"PrimarySelection": primary
		};
		var msg = new Message ("selection_changed", args);
		
		this.Send (msg);
	};
	
	this.ShowContextMenu = function (x, y, id) {
		var args = {
			"ComponentId" : id,
			"X" : x,
			"Y" : y
		};
		
		var msg = new Message ("context_menu_request", args);
		this.Send (msg);
	};
}

function Message (name, args) {
	this.MsgName = name;
	this.Arguments = JSON.stringify (args);
}
