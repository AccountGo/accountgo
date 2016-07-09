import * as React from "react";

export default class SelectMeasurement extends React.Component<any, {}>{
    onChangeMeasurement(e) {
        this.props.store.updateLineItem(this.props.row, "measurementId", e.target.value);
    }
    render() {
        var options = [];
        // TODO: replace with real values;
        options.push(<option key="1" value="1"> Measurement #1 </option>);
        options.push(<option key="2" value="2"> Measurement #2 </option>);
        options.push(<option key="3" value="3"> Measurement #3 </option>);
        return (
            <select onChange={this.onChangeMeasurement.bind(this) }>
                {options}
            </select>
        );
    }
}