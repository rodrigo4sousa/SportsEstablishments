import type { Metadata } from "next";
import { ContactEmailLink, LegalDocument, LegalList, LegalSection, LegalSubsection } from "@/features/legal/components/legal-document";
import { DATA_CONTROLLER_NAME } from "@/features/legal/legal-info";

export const metadata: Metadata = {
  title: "Política de Privacidade",
  description: "Que dados pessoais esta demonstração trata, para quê e que direitos tens.",
};

export default function Page() {
  return (
    <LegalDocument title="Política de Privacidade">
      <p>
        Esta aplicação é uma demonstração pessoal, sem fins comerciais. Esta página explica que dados pessoais trata, para quê e que
        direitos tens ao abrigo do Regulamento Geral sobre a Proteção de Dados (RGPD, Regulamento (UE) 2016/679).
      </p>

      <LegalSection title="Responsável pelo tratamento">
        <p>
          {DATA_CONTROLLER_NAME}. Para assuntos de proteção de dados, escreve para <ContactEmailLink />.
        </p>
      </LegalSection>

      <LegalSection title="Que dados tratamos, para quê e com que base legal">
        <LegalSubsection title="Conta">
          <p>
            Nome, email, telemóvel (opcional), perfil, data de registo, a versão dos Termos aceite e a data dessa aceitação, tentativas
            de entrada falhadas e bloqueios temporários. A palavra-passe é guardada apenas sob a forma de hash, nunca em texto. O
            telemóvel fica guardado, mas hoje não é mostrado a ninguém na aplicação nem usado para te contactar.
          </p>
          <p>Para criar e gerir a conta e iniciar sessão. Base legal: execução do contrato (os Termos que aceitas ao criar conta).</p>
        </LegalSubsection>

        <LegalSubsection title="Sessões">
          <p>
            Para cada sessão: um hash do token de renovação, a data de criação, de expiração e de revogação. O valor do token só existe
            no cookie do teu browser.
          </p>
          <p>Para te manter com sessão iniciada. Base legal: execução do contrato.</p>
        </LegalSubsection>

        <LegalSubsection title="Reservas">
          <p>
            Quem reservou, o campo, a data e a hora, o preço, o estado, a data e o motivo de um cancelamento, e as faltas. O dono do clube
            vê o teu nome e o número de faltas que tens nos clubes dele; não vê o teu email nem o teu telemóvel.
          </p>
          <p>Para fazer e gerir as reservas. Base legal: execução do contrato.</p>
        </LegalSubsection>

        <LegalSubsection title="Clubes e donos">
          <p>
            Nome do clube, morada, localidade, telefone e email de contacto, que são públicos na página do clube. A administração vê o nome
            e o email dos donos. Quando um dono é desativado ou reativado, ficam registados a data, o motivo e quem o fez.
          </p>
          <p>
            Para mostrar os clubes e administrar a plataforma. Base legal: execução do contrato e, no histórico de desativações, interesse
            legítimo em manter esse registo.
          </p>
          <p>A administração da plataforma vê apenas contagens sobre os jogadores, nunca o nome, o email ou o telemóvel deles.</p>
        </LegalSubsection>

        <LegalSubsection title="Emails">
          <p>
            Confirmações e cancelamentos de reservas, um lembrete 3 horas antes do início, o resumo de uma reserva semanal, o aviso ao dono
            de uma nova reserva (cada clube pode desligá-lo), e os emails de recuperação e de alteração da palavra-passe. Cada email usa o
            teu endereço e o teu nome, lidos da conta no momento do envio, e os dados do que trata (clube, morada, campo, data, hora, preço
            e motivo de cancelamento, ou a ligação para definir uma nova palavra-passe). Os emails ficam numa fila na base de dados, com o
            identificador da conta e os detalhes da reserva, mas sem o endereço.
          </p>
          <p>Para te informar sobre as tuas reservas e a tua conta. Base legal: execução do contrato.</p>
        </LegalSubsection>

        <LegalSubsection title="Endereço IP">
          <p>
            A API limita o número de pedidos por endereço IP (em IPv6, por rede /64). Os contadores ficam em memória, em janelas de um
            minuto. Quando alguém ultrapassa um limite, o endereço (ou a rede) e o caminho pedido ficam no registo (log) da aplicação. A
            aplicação não regista o IP em cada pedido; o IP também chega aos fornecedores de alojamento indicados abaixo.
          </p>
          <p>Para proteger o serviço contra abusos. Base legal: interesse legítimo em manter o serviço seguro.</p>
        </LegalSubsection>
      </LegalSection>

      <LegalSection title="Clientes sem conta">
        <p>
          Um dono pode reservar em nome de alguém que não tem conta, escrevendo o nome e o telemóvel dessa pessoa. Estes dados ficam com a
          reserva e só o dono desse clube os vê; a pessoa não recebe emails.
        </p>
        <p>
          É o dono do clube que decide introduzir estes dados e é ele quem responde por eles perante o cliente: tem de ter motivo para os
          recolher, informar o cliente e mantê-los corretos. A plataforma guarda-os para que o dono possa gerir a reserva, e tem nisso um
          interesse legítimo. Se fores um destes clientes, podes contactar o clube ou escrever para <ContactEmailLink />.
        </p>
      </LegalSection>

      <LegalSection title="Quem recebe os dados">
        <p>Estes fornecedores tratam dados em nome do responsável, para que o serviço funcione:</p>
        <LegalList>
          <li>
            <strong className="font-medium">Vercel</strong> aloja o site, que é servido pela rede global da Vercel. Recebe todos os pedidos do
            browser: o endereço IP, os cookies de sessão e o que preencheres nos formulários. As funções do site correm em Washington DC (EUA).
          </li>
          <li>
            <strong className="font-medium">Render</strong> aloja a API. Recebe os pedidos reencaminhados pelo site, com os mesmos dados, e
            guarda os registos (logs) da aplicação. Em Frankfurt (Alemanha).
          </li>
          <li>
            <strong className="font-medium">Supabase</strong> aloja a base de dados, onde ficam guardados os dados descritos nesta página. Na
            Irlanda.
          </li>
          <li>
            <strong className="font-medium">Brevo</strong> envia os emails. Recebe o endereço e o nome do destinatário e o conteúdo de cada
            email. Em França.
          </li>
          <li>
            <strong className="font-medium">GitHub</strong> guarda o código e corre a publicação de novas versões. Não recebe dados dos
            utilizadores.
          </li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Onde ficam os dados e transferências para fora da União Europeia">
        <p>
          Os dados ficam guardados na base de dados, na Irlanda. A API corre em Frankfurt e os emails são enviados através da Brevo, em França.
        </p>
        <p>
          O browser só fala com o site, e os pedidos à API passam pela infraestrutura da Vercel, uma empresa dos Estados Unidos, cujas funções para
          este site correm em Washington DC. Por isso, os pedidos com dados pessoais (entrar, reservar) passam pela rede de uma empresa dos Estados
          Unidos e há transferência de dados para esse país.
        </p>
        <p>
          Cada fornecedor indica, nos seus próprios documentos, a salvaguarda que aplica às transferências para fora do Espaço Económico Europeu:
        </p>
        <LegalList>
          <li>
            <strong className="font-medium">Vercel</strong> (Vercel Inc., Estados Unidos): as Cláusulas Contratuais-Tipo aprovadas pela Comissão
            Europeia (Decisão 2021/914), no seu Data Processing Addendum, e a declaração de adesão ao EU-U.S. Data Privacy Framework, na sua Privacy
            Notice.
          </li>
          <li>
            <strong className="font-medium">Render</strong> (Render Services, Inc., Estados Unidos): o EU-U.S. Data Privacy Framework e, se este não
            se aplicar, as mesmas Cláusulas Contratuais-Tipo, no seu Data Processing Addendum. Esse documento indica que as operações principais da
            Render são nos Estados Unidos, embora a API corra em Frankfurt.
          </li>
          <li>
            <strong className="font-medium">Supabase</strong> (Supabase Pte. Ltd., Singapura): as Cláusulas Contratuais-Tipo, no seu Data Processing
            Addendum. A sua política de privacidade refere transferências para os Estados Unidos e para Singapura.
          </li>
          <li>
            <strong className="font-medium">Brevo</strong> (sociedade francesa, com sede em Paris): os seus termos indicam alojamento em França e na
            Bélgica e admitem tratamento fora do Espaço Económico Europeu por subcontratantes, com Cláusulas Contratuais-Tipo e medidas suplementares
            e, para os Estados Unidos, o EU-U.S. Data Privacy Framework enquanto se mantiver válido.
          </li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Durante quanto tempo">
        <LegalList>
          <li>
            Não há um prazo definido nem eliminação automática: contas, reservas, dados de clientes sem conta, tokens de sessão expirados e
            mensagens da fila de emails ficam guardados enquanto a demonstração existir, salvo se apagares a conta ou pedires a eliminação.
          </li>
          <li>
            Uma conta desativada continua guardada. Uma conta apagada fica sem dados pessoais. As reservas nunca são apagadas pela aplicação, só
            mudam de estado.
          </li>
          <li>Os contadores de pedidos por IP duram um minuto e perdem-se quando a API reinicia.</li>
          <li>O tempo durante o qual os registos (logs) ficam guardados no Render e na Vercel está a confirmar.</li>
          <li>Não são feitas cópias de segurança da base de dados.</li>
        </LegalList>
      </LegalSection>

      <LegalSection title="Cookies e armazenamento no browser">
        <p>
          Usamos apenas cookies estritamente necessários para manter a sessão e, por isso, não é preciso um aviso de cookies. Não há cookies
          de análise nem de publicidade, e o site não carrega conteúdos de outros domínios. Estes cookies só existem depois de entrares ou
          criares conta, e nenhum deles pode ser lido pelo JavaScript da página (httpOnly).
        </p>
        <LegalList>
          <li>
            <code>access_token</code>: identifica a tua sessão em cada pedido. Dura 15 minutos. Caminho <code>/</code>, httpOnly, Secure,
            SameSite=Lax.
          </li>
          <li>
            <code>refresh_token</code>: renova a sessão sem pedir de novo a palavra-passe. Dura 7 dias, no máximo, desde que entraste. Caminho{" "}
            <code>/api/auth</code>, httpOnly, Secure, SameSite=Strict.
          </li>
        </LegalList>
        <p>A aplicação não guarda dados em localStorage nem em sessionStorage.</p>
      </LegalSection>

      <LegalSection title="Os teus direitos">
        <p>
          Tens direito de acesso, de retificação, de apagamento, de limitação do tratamento, de portabilidade e de oposição. Na aplicação, em
          «A minha conta», podes exercer dois deles sem escrever a ninguém:
        </p>
        <LegalList>
          <li>
            <strong className="font-medium">Acesso e portabilidade.</strong> «Exportar os meus dados» descarrega um ficheiro JSON com o teu perfil
            (nome, email, telemóvel, perfil e data de registo), as tuas reservas (clube, campo, datas, preço e estado) e a versão dos Termos que
            aceitaste e quando. Não inclui a palavra-passe, tokens, identificadores internos nem dados de outras pessoas.
          </li>
          <li>
            <strong className="font-medium">Apagamento.</strong> «Apagar a minha conta» pede a tua palavra-passe e, de uma só vez, cancela as tuas
            reservas futuras e liberta esses horários, substitui o teu nome por «Conta apagada» e o teu email por um endereço que não recebe
            correio, apaga o telemóvel, a palavra-passe, o registo da aceitação dos Termos e os emails ainda por enviar, e termina todas as sessões. O teu email fica livre
            para uma nova conta. As reservas passadas ficam para o histórico dos clubes, sem te identificar. Um motivo escrito por um clube ao cancelar
            uma reserva, ou por quem administra a plataforma ao desativar um dono, não é alterado. Não enviamos um email de confirmação: a
            confirmação aparece no ecrã.
          </li>
          <li>
            Uma conta de dono com clubes não pode ser apagada na aplicação, porque um clube tem sempre dono: escreve para <ContactEmailLink />. A
            conta de administração não pode ser apagada.
          </li>
        </LegalList>
        <p>
          Podes ver as tuas reservas em «As minhas reservas» e definir uma nova palavra-passe. A aplicação não tem uma função para editar o perfil.
          Para este ou para qualquer outro direito, escreve para <ContactEmailLink /> a partir do email da tua conta; o pedido é tratado à mão.
          As contas de demonstração são partilhadas e não têm um titular real.
        </p>
      </LegalSection>

      <LegalSection title="Queixas">
        <p>Tens o direito de apresentar queixa à Comissão Nacional de Proteção de Dados (CNPD).</p>
      </LegalSection>

      <LegalSection title="Alterações">
        <p>
          Esta política pode mudar. Quando isso acontecer, a data e a versão no topo desta página são atualizadas. As contas criadas antes de
          existir esta página não têm registo de aceitação.
        </p>
      </LegalSection>
    </LegalDocument>
  );
}
