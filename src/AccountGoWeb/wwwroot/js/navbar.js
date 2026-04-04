function loadNavbarState() {
    loadExpandedNavbar();
    loadActiveNavLink();

    $("#sidebar").css("opacity", 1);
    $(".sub-menu").css("opacity", 1);
}

function loadExpandedNavbar() {
    const navbarState = localStorage.getItem("navbarState");
    if (navbarState) {
        const expandedNavbar = JSON.parse(navbarState);
        expandedNavbar.forEach(index => {
            $(".sub-menu").eq(index).children("ul").show();
            $(".sub-menu").eq(index).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
        })
    }
}

function loadActiveNavLink() {
    $("#sidebar .nav-link").removeClass("active");
    $("#sidebar .nav-link").each(function () {
        if (window.location.href.includes($(this).attr("href"))) {
            $(this).addClass("active");
        }
    });
}

function saveExpandedNavbar() {
    const expandedNavbar = [];
    $(".sub-menu").each(function (index) {
        if ($(this).children("ul").is(":visible")) {
            expandedNavbar.push(index);
        }
    });
    localStorage.setItem("navbarState", JSON.stringify(expandedNavbar));
}

$("#sidebar .nav-link").click(function () {
    $("#sidebar .nav-link").removeClass("active");
    $(this).addClass("active");
});

$(".sub-menu a").click(function () {
    $(this).parent(".sub-menu").children("ul").slideToggle("100", function () {
        saveExpandedNavbar();
    });
    $(this).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
});

loadNavbarState();
