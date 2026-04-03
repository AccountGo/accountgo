function loadNavbarState() {
    const navbarState = localStorage.getItem("navbarState");
    if (navbarState) {
        const expandedNavbar = JSON.parse(navbarState);
        expandedNavbar.forEach(index => {
            $(".sub-menu").eq(index).children("ul").show();
            $(".sub-menu").eq(index).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
        })
    }
}

function saveNavbarState() {
    const expandedNavbar = [];
    $(".sub-menu").each(function (index) {
        if ($(this).children("ul").is(":visible")) {
            expandedNavbar.push(index);
        }
    });
    localStorage.setItem("navbarState", JSON.stringify(expandedNavbar));
}

$(".sub-menu ul").hide();
$(".sub-menu a").click(function () {
    $(this).parent(".sub-menu").children("ul").slideToggle("100", function () {
        saveNavbarState();
    });
    $(this).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
});

loadNavbarState();
