import * as React from "react";

type HomeProps = {
    // specify the props type here
};

type HomeState = {
    // specify the state type here
};

class Home extends React.Component<HomeProps, HomeState> {
    render() {
        return (
           <div className="container">
                <nav className="navbar navbar-expand-sm navbar-light bg-info p-3">
                    <div className="container-fluid">
                        <a className="navbar-brand" href="/">Good Books</a>
                        <button
                            className="navbar-toggler"
                            type="button"
                            data-bs-toggle="collapse"
                            data-bs-target="#navbarNavDropdown"
                            aria-controls="navbarNavDropdown"
                            aria-expanded="false"
                            aria-label="Toggle navigation"
                        >
                            <span className="navbar-toggler-icon"></span>
                        </button>

                        <div className="collapse navbar-collapse" id="navbarNavDropdown">
                            <ul className="navbar-nav ms-auto">
                                <li className="nav-item">
                                    <a
                                    className="nav-link mx-2 active"
                                    aria-current="page"
                                    href="/journal-entry"
                                    >Journal Entry</a
                                    >
                                </li>
                                <li className="nav-item dropdown">
                                    <a
                                    className="nav-link mx-2 dropdown-toggle"
                                    href="#"
                                    id="navbarDropdownMenuLink"
                                    role="button"
                                    data-bs-toggle="dropdown"
                                    aria-expanded="false"
                                    >
                                    Purchasing
                                    </a>
                                    <ul
                                    className="dropdown-menu"
                                    aria-labelledby="navbarDropdownMenuLink"
                                    >
                                        <li className="nav-item">
                                            <a className="nav-link mx-2" href="/purchasing-invoice">Purchase Invoice</a>
                                        </li>
                                        <li className="nav-item">
                                            <a className="nav-link mx-2" href="/purchase-order">Purchase Order</a>
                                        </li>
                                    </ul>
                                </li>
                                <li className="nav-item dropdown">
                                    <a
                                    className="nav-link mx-2 dropdown-toggle"
                                    href="#"
                                    id="navbarDropdownMenuLink"
                                    role="button"
                                    data-bs-toggle="dropdown"
                                    aria-expanded="false"
                                    >
                                    Sales
                                    </a>
                                    <ul
                                    className="dropdown-menu"
                                    aria-labelledby="navbarDropdownMenuLink"
                                    >
                                        <li className="nav-item">
                                            <a className="nav-link mx-2" href="/sales-invoice">Sales Invoice</a>
                                        </li>
                                        <li className="nav-item">
                                            <a className="nav-link mx-2" href="/sales-order">Sales Order</a>
                                        </li>
                                    </ul>
                                </li>
                            </ul>
                        </div>
                     </div> {/*<!-- container-fluid --> */}
                </nav>
            </div>

        );
    }
}

export default Home;