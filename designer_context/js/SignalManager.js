function SignalManager () {
	this.Send = function (str) {
		jQuery ("title").html (str);
	};
	
	this.ChangeSelection = function (idArr, primary) {
		var args = JSON.stringify ({
			"SelectedIds" : idArr,
			"PrimarySelection": primary
		});
		var msg = new Message ("selection_changed", args);
		
		this.Send (JSON.stringify (msg));
	};
}

function Message (name, args) {
	this.MsgName = name;
	this.Arguments = args;
}
