import Heading from "./components/Heading"
import Section from "./components/Section"
import Counter from "./components/Counter"
import List from "./components/List"
// import Home from "./components/Home/Index"
import ObservedJournalEntry from "./components/Financials/JournalEntry"

function App() {

  return (
    <>
      <Heading title={"HELLO"} />
      <Section title={"This is the section title"}>
        <p>Whatever is here is considered children</p>
      </Section>
      <Counter />
      <List
        items={["Coffee", "Tacos", "Code"]}
        render={(item: string) => <strong>{item}</strong>}
      />
      <ObservedJournalEntry />  
    </>
  )
}

export default App
