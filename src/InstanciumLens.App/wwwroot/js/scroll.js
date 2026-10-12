// Scrolls the chat viewport to the latest token during streaming.
window.scrollChatToBottom = function (elementId) {
    var element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};