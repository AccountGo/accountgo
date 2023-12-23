import * as React from "react";
import * as ReactDOM from "react-dom";

class Home extends React.Component<any, {}> {
    render() {
        return (
            <div>
                Tiles or widgets here!
            </div>
        );
    }
}

ReactDOM.render(
    <Home />,
    document.getElementById("home")
);

export default Home;