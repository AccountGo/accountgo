import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectLineMeasurement extends React.Component<any, {}>{
    onChangeMeasurement(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "measurementId", e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.measurements.map(function (measurement) {
            return (
                options.push(<option key={ measurement.id } value={ measurement.id } > { measurement.description } </option>)
            );
        });

        return (
            <select value={this.props.selected} id={this.props.controlId} onChange={this.onChangeMeasurement.bind(this) } className="form-control select2">
                <option key={ -1 }></option>
                {options}
            </select>
        );
    }
}