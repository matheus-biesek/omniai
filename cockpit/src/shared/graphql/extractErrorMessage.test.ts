import { describe, expect, it } from "vitest";
import { ClientError } from "graphql-request";
import { extractErrorMessage } from "./extractErrorMessage";

function clientError(errors: { message: string }[] | undefined): ClientError {
  return new ClientError(
    { status: 200, headers: new Headers(), errors } as ConstructorParameters<typeof ClientError>[0],
    { query: "mutation { x }" },
  );
}

describe("extractErrorMessage", () => {
  it("usa a mensagem do primeiro erro GraphQL", () => {
    const error = clientError([{ message: "Já existe um projeto chamado 'x'." }, { message: "segundo" }]);

    expect(extractErrorMessage(error, "fallback")).toBe("Já existe um projeto chamado 'x'.");
  });

  it("usa o fallback quando ClientError nao tem erros", () => {
    expect(extractErrorMessage(clientError(undefined), "fallback")).toBe("fallback");
    expect(extractErrorMessage(clientError([]), "fallback")).toBe("fallback");
  });

  it("usa o fallback para erro de rede ou qualquer outra coisa", () => {
    expect(extractErrorMessage(new TypeError("Failed to fetch"), "fallback")).toBe("fallback");
    expect(extractErrorMessage("string", "fallback")).toBe("fallback");
    expect(extractErrorMessage(undefined, "fallback")).toBe("fallback");
  });
});
