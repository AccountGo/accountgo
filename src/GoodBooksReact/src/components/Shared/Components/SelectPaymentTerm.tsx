import * as React from "react";
import {observer} from "mobx-react";

class SelectPaymentTerm extends React.Component<any, {}>{
    onChangePaymentTerm(e: any) {
        this.props.store.changedPaymentTerm(e.target.value);
    }

    render() {
        const options: JSX.Element[] = [];
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
const ObservedSelectPaymentTerm = observer(SelectPaymentTerm);

export default ObservedSelectPaymentTerm;