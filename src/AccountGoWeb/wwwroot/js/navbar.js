function loadNavbarState() {
    loadExpandedNavbar();
    loadActiveNavLink();

    $("#sidebar").css("opacity", 1);
    $(".sub-menu").css("opacity", 1);
}

function loadExpandedNavbar() {
    const navbarState = sessionStorage.getItem("navbarState");

    if (navbarState) {
        const expandedNavbar = JSON.parse(navbarState);

        expandedNavbar.forEach(index => {
            $(".sub-menu").eq(index).children("ul").show();
            $(".sub-menu").eq(index).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
        })
    }
}

function loadActiveNavLink() {
    const currenthPath = window.location.pathname;

    $("#sidebar .nav-link").removeClass("nav-link-active");

    $("#sidebar .nav-link").each(function () {
        const path = $(this).attr("href");
        if (currenthPath === path) {
            $(this).addClass("nav-link-active");
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
    sessionStorage.setItem("navbarState", JSON.stringify(expandedNavbar));
}

$("#sidebar .nav-link").click(function () {
    $("#sidebar .nav-link").removeClass("nav-link-active");
    $(this).addClass("nav-link-active");
});

$(".sub-menu a").click(function () {
    $(this).parent(".sub-menu").children("ul").slideToggle("100", function () {
        saveExpandedNavbar();
    });
    $(this).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
});

loadNavbarState();
