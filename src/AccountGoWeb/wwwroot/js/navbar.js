function loadNavbarState() {
    loadExpandedNavbar();
    loadActiveNavLink();
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
    const activeNavLink = localStorage.getItem("activeNavLink");
    if (activeNavLink) {
        $(".nav-link").removeClass("active");
        $(".nav-link").filter(function () {
            return $(this).attr("href") === activeNavLink;
        }).addClass("active");
    }
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


$(".sub-menu ul").hide();

$(".sub-menu .nav-link").click(function () {
    $(".nav-link").removeClass("active");
    $(this).addClass("active");
    localStorage.setItem("activeNavLink", $(this).attr("href"));
});

$(".sub-menu a").click(function () {
    $(this).parent(".sub-menu").children("ul").slideToggle("100", function () {
        saveExpandedNavbar();
    });
    $(this).find(".ml-auto").toggleClass("fa-caret-up fa-caret-down");
    $(this).find(".nav-link").toggleClass("active");
});

loadNavbarState();
