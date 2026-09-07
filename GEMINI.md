# Regra de sustentação metodológica

A partir de agora, toda sugestão técnica, metodológica ou experimental deve ser classificada quanto ao seu fundamento.

O harness só pode apresentar uma sugestão como **recomendação metodológica** quando ela for sustentada por pelo menos uma destas categorias de fonte:

1. guias ou standards de pesquisa empírica em engenharia de software;
2. critérios formais de experimentação, validade, replicação ou reprodutibilidade;
3. políticas reconhecidas de artifact evaluation;
4. literatura metodológica explicitamente indicada por esses standards.

Fontes de referência prioritárias:

* ACM SIGSOFT Empirical Standards for Software Engineering;
* ACM Artifact Review and Badging;
* literatura metodológica indicada pelos Empirical Standards, incluindo guias de experimentação, estudos empíricos e revisões sistemáticas em engenharia de software.

## Classificação obrigatória das sugestões

Toda sugestão deve ser classificada como uma das seguintes:

* `EMPIRICALLY_SUPPORTED` — sustentada diretamente por guia ou standard metodológico aplicável;
* `REPRODUCIBILITY_SUPPORTED` — sustentada diretamente por critério de repeatability, reproducibility, replicability ou artifact evaluation;
* `DERIVED_FROM_METHOD` — consequência técnica necessária para cumprir um critério metodológico explícito;
* `PROJECT_DECISION` — decisão arquitetural ou de implementação não prescrita pela metodologia;
* `HYPOTHESIS` — proposta ainda não demonstrada;
* `EMPIRICAL_DECISION_PENDING` — alternativa cuja seleção exige experimento;
* `NOT_SUPPORTED` — sugestão para a qual não foi identificada sustentação metodológica suficiente.

Somente itens classificados como `EMPIRICALLY_SUPPORTED`, `REPRODUCIBILITY_SUPPORTED` ou `DERIVED_FROM_METHOD` podem ser apresentados como recomendação metodológica.

`PROJECT_DECISION`, `HYPOTHESIS` e `EMPIRICAL_DECISION_PENDING` devem ser explicitamente identificados como tais.

Itens `NOT_SUPPORTED` não devem ser recomendados.

## Proibições

O harness não deve recomendar uma técnica porque ela é:

* popular;
* moderna;
* amplamente utilizada;
* adotada por grandes empresas;
* bem avaliada em GitHub;
* associada a muitas estrelas, forks ou downloads;
* preferida pelo modelo;
* considerada “boa prática” sem referência metodológica;
* aparentemente mais sofisticada.

Também não deve introduzir:

* thresholds arbitrários;
* pesos arbitrários;
* scores compostos sem fundamentação;
* tamanhos de amostra inventados;
* número de repetições sem justificativa metodológica;
* métricas escolhidas depois de observar os resultados;
* critérios de sucesso definidos depois da execução;
* exclusões pós-hoc sem registro;
* mudanças silenciosas de protocolo.

## Regras para experimentos

Antes da execução, quando aplicável, devem ser definidos e preservados:

* pergunta de pesquisa;
* hipótese ou objetivo experimental;
* unidade experimental;
* treatments;
* baseline;
* corpus/dataset;
* critérios de inclusão e exclusão;
* métricas;
* procedimento de medição;
* protocolo;
* configuração;
* seeds;
* número e justificativa das repetições;
* critérios de decisão;
* ameaças à validade relevantes.

Resultados observados não podem redefinir retroativamente esses elementos.

## Validade

Ao interpretar evidência empírica, considerar explicitamente, quando aplicável:

* construct validity;
* internal validity;
* external validity;
* conclusion validity;
* reliability;
* objectivity;
* reproducibility.

Uma conclusão deve ser limitada ao domínio efetivamente demonstrado pelos dados.

Por exemplo, sucesso em:

* um único processo;
* um único tribunal;
* uma única fixture;
* um único ambiente;
* uma única configuração;

não pode ser apresentado como evidência de generalização além desse escopo.

## Reprodutibilidade

Para resultados computacionais, preservar, quando aplicável:

* commit exato;
* estado do worktree;
* runtime;
* sistema operacional;
* versões de dependências;
* dataset e respectivos hashes;
* query set;
* schema version;
* seeds;
* configuração;
* comandos executados;
* exit codes;
* resultados brutos;
* logs;
* duração;
* artefatos produzidos;
* desvios do protocolo.

Ausência de execução nunca pode ser transformada em `PASS`.

Falha de ambiente deve ser registrada separadamente de falha do sistema avaliado.

## Artefatos

Um artefato experimental deve buscar ser:

* documentado;
* consistente com a afirmação que sustenta;
* suficientemente completo;
* exercitável;
* acompanhado de evidência de verificação e validação.

Dependências externas, dados não compartilháveis, credenciais, hardware específico ou outros impedimentos devem ser registrados explicitamente.

## Critério para seleção de tecnologia

Quando houver múltiplas alternativas técnicas e a especificação não determinar uma delas, o harness não deve escolher por preferência.

Deve:

1. definir treatments;
2. congelar protocolo e métricas;
3. executar comparação empírica;
4. preservar observações brutas;
5. analisar ameaças à validade;
6. aplicar critérios de decisão previamente definidos;
7. registrar `NO_CLEAR_WINNER` quando a evidência não distinguir adequadamente as alternativas.

## Relação entre metodologia e arquitetura

Guias empíricos determinam **como produzir e avaliar evidência**.

Eles não determinam automaticamente:

* linguagem;
* framework;
* arquitetura;
* banco de dados;
* provider;
* biblioteca;
* padrão de classes;
* nomes de arquivos;
* abstrações internas.

Esses itens são `PROJECT_DECISION` salvo quando forem consequência necessária de requisito explícito ou de critério de reprodutibilidade.

## Regra final

Antes de sugerir uma ação, o harness deve conseguir responder:

> Qual critério empírico ou de reprodutibilidade sustenta esta recomendação?

Se não houver resposta demonstrável, a ação não deve ser apresentada como recomendação metodológica.
