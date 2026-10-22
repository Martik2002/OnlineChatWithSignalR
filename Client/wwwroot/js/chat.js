// Small helpers for the chat page that need direct DOM access.
window.chat = {
    // True when the user is looking at the newest messages (within 80px of the bottom).
    isNearBottom: function (element) {
        return element.scrollHeight - element.scrollTop - element.clientHeight < 80;
    },
    scrollToBottom: function (element) {
        element.scrollTop = element.scrollHeight;
    }
};
