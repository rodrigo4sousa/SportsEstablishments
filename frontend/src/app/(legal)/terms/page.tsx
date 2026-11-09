import type { Metadata } from "next";
import Link from "next/link";
import { ContactEmailLink, LegalDocument, LegalList, LegalSection } from "@/features/legal/components/legal-document";
import { PRIVACY_PATH } from "@/features/legal/legal-info";

export const metadata: Metadata = {
  title: "Termos e Condições",
  description: "As regras de utilização desta demonstração de reserva de campos.",
};

export default function Page() {
  return (
    <LegalDocument title="Termos e Condições">
      <p>Ao criares uma conta, aceitas estes Termos. Se não concordares com eles, não cries conta.</p>

      <LegalSection title="O que é este serviço">
        <p>
          Esta aplicação é uma demonstração pessoal, sem fins comerciais, feita para mostrar um projeto de programação. Permite
          pesquisar clubes e reservar campos de futsal, futebol 7, padel e ténis.
        </p>
        <LegalList>
          <li>Os clubes, campos e reservas que aqui aparecem podem ser fictícios.</li>
          <li>É gratuita e não tem qualquer garantia. Pode ser alterada ou desligada a qualquer momento, sem aviso.</li>
          <li>Não são feitas cópias de segurança: os dados podem perder-se.</li>
          <li>
            A API adormece ao fim de 15 minutos sem utilização e demora cerca de um minuto a acordar, por isso o primeiro pedido
            pode ser lento. Enquanto dorme, os emails ficam à espera, e um lembrete cuja reserva já tenha começado não é enviado.
          </li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Contas">
        <LegalList>
          <li>Há contas de jogador e de dono de clube. Um email corresponde a uma só conta e a um só perfil.</li>
          <li>Uma conta de jogador serve em todos os clubes da plataforma.</li>
          <li>Mantém a tua palavra-passe em segredo. Se achares que outra pessoa a usou, altera-a e avisa-nos.</li>
          <li>
            As contas de demonstração têm palavras-passe públicas e são partilhadas: qualquer pessoa pode entrar nelas e ver ou
            mudar o que lá estiver. Não coloques dados reais nessas contas.
          </li>
          <li>
            A conta de um dono pode ser desativada pela administração da plataforma, por exemplo se não cumprir estes Termos. Uma
            conta desativada não consegue entrar e os seus clubes deixam de aparecer ao público; as reservas já feitas mantêm-se
            confirmadas.
          </li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Reservas">
        <LegalList>
          <li>Podes reservar de hoje até 30 dias à frente, nos horários de cada campo (30, 60 ou 90 minutos). As horas são as de Lisboa.</li>
          <li>
            A aplicação não processa pagamentos: o pagamento é feito no local, diretamente ao clube. Cada reserva guarda o preço do
            momento em que foi feita.
          </li>
          <li>
            Podes cancelar uma reserva até ao fim da janela de cancelamento do clube (24 horas antes do início, por omissão; cada
            clube define a sua, entre 1 e 168 horas). Conta a janela em vigor no momento em que cancelas. Depois disso, a aplicação
            não permite cancelar.
          </li>
          <li>O clube pode cancelar uma reserva que ainda não tenha começado, indicando o motivo; recebes um email.</li>
          <li>
            Depois de uma reserva terminar, o dono do clube pode marcá-la como falta e anular essa marcação. O dono vê quantas
            faltas tens nos clubes dele.
          </li>
          <li>
            Podes reservar o mesmo horário por 2 a 4 semanas seguidas. As datas livres ficam reservadas e as outras ficam de fora,
            e são indicadas antes de confirmares. Cada data é uma reserva independente, com o seu preço, que se cancela sozinha.
          </li>
          <li>As reservas nunca são apagadas: mudam de estado (confirmada, cancelada pelo jogador, cancelada pelo clube ou falta).</li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Responsabilidades dos donos de clubes">
        <LegalList>
          <li>Tens de manter corretos e atualizados os dados dos teus clubes: nome, morada, contactos, campos, horários e preços. Estes dados são públicos.</li>
          <li>
            Se reservares em nome de um cliente sem conta, só deves introduzir o nome e o telemóvel dele se tiveres motivo para o
            fazer, depois de o informares. Respondes perante esse cliente por esses dados, como explica a{" "}
            <Link href={PRIVACY_PATH} className="underline underline-offset-4">
              Política de Privacidade
            </Link>
            .
          </li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Utilização aceitável">
        <p>Não podes:</p>
        <LegalList>
          <li>tentar aceder a contas ou dados de outras pessoas, ou contornar as proteções da aplicação;</li>
          <li>sobrecarregar o serviço ou extrair os seus dados de forma automatizada;</li>
          <li>fazer-te passar por outra pessoa ou criar reservas falsas;</li>
          <li>escrever nos campos de texto conteúdo ilegal ou ofensivo, ou dados pessoais de outras pessoas sem motivo.</li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Disponibilidade e responsabilidade">
        <p>
          O serviço é fornecido gratuitamente, tal como está, sem garantia de que esteja disponível, de que não tenha erros ou de que
          as informações dos clubes estejam corretas. Como é uma demonstração, confirma diretamente com o clube qualquer reserva de
          que dependas.
        </p>
        <p>
          Na medida em que a lei o permita, o responsável por esta demonstração não responde por prejuízos causados pela
          indisponibilidade do serviço, pela perda de dados ou por informações incorretas. Nada nestes Termos exclui ou limita
          direitos ou responsabilidades que a lei não permita excluir ou limitar.
        </p>
      </LegalSection>

      <LegalSection title="Alterações">
        <p>
          Estes Termos podem mudar. Quando isso acontecer, a data e a versão no topo desta página são atualizadas. Cada conta nova
          fica com o registo da versão que aceitou.
        </p>
      </LegalSection>

      <LegalSection title="Lei aplicável">
        <p>Estes Termos regem-se pela lei portuguesa.</p>
      </LegalSection>

      <LegalSection title="Contacto">
        <p>
          Para qualquer questão sobre estes Termos, escreve para <ContactEmailLink />.
        </p>
      </LegalSection>
    </LegalDocument>
  );
}
