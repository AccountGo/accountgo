function getParameterByName(name, url) {
    if (!url) url = window.location.href;
    name = name.replace(/[\[\]]/g, "\\$&");
    var regex = new RegExp("[?&]" + name + "(=([^&#]*)|&|#|$)"),
        results = regex.exec(url);
    if (!results) return null;
    if (!results[2]) return '';
    return decodeURIComponent(results[2].replace(/\+/g, " "));
}

function setFormDisabled(divId) {
    var nodes = document.getElementById(divId).getElementsByTagName('*');
    for (var i = 0; i < nodes.length; i++) {
        nodes[i].className = nodes[i].className + " disabledControl";
    }
}

function setFormEnabled(divId) {
    // Remove " disabledControl" from current className
    var nodes = document.getElementById(divId).getElementsByTagName('*');
    for (var i = 0; i < nodes.length; i++) {
        var subStringLength = nodes[i].className.length - " disabledControl".length;
        nodes[i].className = nodes[i].className.substring(0, subStringLength);
    }
}