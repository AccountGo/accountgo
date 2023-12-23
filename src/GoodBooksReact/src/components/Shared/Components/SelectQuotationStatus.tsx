import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectQuotationStatus extends React.Component<any, {}> {
    onChangeQuoteStatus(e: any) {
        this.props.store.changedQuoteStatus(e.target.value);    
    }

    render() {
        var options: any = [];
        this.props.store.commonStore.salesQuotationStatus.map(function (item: any) {
            return (
                options.push(<option key={ item.id } value={ item.id } > { item.description } </option>)
            );
        });

        return (
           
            <select value={this.props.selected} id={this.props.controlId} onChange={this.onChangeQuoteStatus.bind(this) } className="form-control select2">
                <option key={ -1 }></option>
                {options}
            </select>
        );

    }


}