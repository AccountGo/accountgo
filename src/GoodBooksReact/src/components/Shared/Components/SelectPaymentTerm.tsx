import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectPaymentTerm extends React.Component<any, {}>{
    onChangePaymentTerm(e: any) {
        this.props.store.changedPaymentTerm(e.target.value);
    }

    render() {
        var options: any = [];
        this.props.store.commonStore.paymentTerms.map(function (term: any) {
            return (
                options.push(<option key={ term.id } value={ term.id } > { term.description } </option>)
            );
        });

        return (
            <select id="optPaymentTerm" value={this.props.selected} onChange={this.onChangePaymentTerm.bind(this) } className="form-control select2">
                <option key={ -1 } value=""></option>
                {options}
            </select>
        );
    }
}