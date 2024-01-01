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
            <div>
                Tiles or widgets here!
            </div>
        );
    }
}

export default Home;