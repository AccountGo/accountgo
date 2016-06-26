import * as React from "react";
import * as ReactDOM from "react-dom";

import Terms from "../Sales/Terms";

class AddSalesOrder extends React.Component<{}, {}> {
    render() {
        return <Terms />;
    }
}

ReactDOM.render(
    <AddSalesOrder />,
    document.getElementById("terms")
);

export default AddSalesOrder;