import { useCallback, useRef, useState } from "react";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import {
  executeOperatorPinConfirm,
  isOperatorPinValidationError,
} from "../utils/uiResilienceHelpers";

export function useOperatorPinPrompt(moduleKey) {
  const { isOperatorPinRequiredFor, operatorPinAutoSubmit } = useSaleSystem();
  const isEnabledForModule = isOperatorPinRequiredFor(moduleKey);

  const [isOpen, setIsOpen] = useState(false);
  const [errorMessage, setErrorMessage] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const pendingActionRef = useRef(null);

  const closeModal = useCallback(() => {
    if (submitting) return;
    setIsOpen(false);
    setErrorMessage("");
    pendingActionRef.current = null;
  }, [submitting]);

  const openForAction = useCallback((executeFn, initialError = "") => {
    pendingActionRef.current = executeFn;
    setErrorMessage(initialError);
    setSubmitting(false);
    setIsOpen(true);
  }, []);

  /**
   * Wrap a save action. If 4-digit PIN is enabled for this module, prompts first.
   * If disabled, runs immediately; if server still returns `requires_operator_pin`,
   * automatically pops open the PIN modal.
   */
  const runWithOperatorPin = useCallback(
    async (executeFn) => {
      if (isEnabledForModule) {
        openForAction(executeFn, "");
        return;
      }

      try {
        await executeFn(undefined);
      } catch (err) {
        if (isOperatorPinValidationError(err)) {
          openForAction(executeFn, "");
          return;
        }
        throw err;
      }
    },
    [isEnabledForModule, openForAction]
  );

  const confirmPin = useCallback(async (pin) => {
    const action = pendingActionRef.current;
    if (!action) {
      setIsOpen(false);
      return;
    }

    setSubmitting(true);
    setErrorMessage("");
    await executeOperatorPinConfirm({
      action,
      pin,
      onSuccess: () => {
        setIsOpen(false);
        setErrorMessage("");
        pendingActionRef.current = null;
      },
      onPinError: (message) => {
        setErrorMessage(message);
      },
      onNonPinError: () => {
        setIsOpen(false);
        setErrorMessage("");
        pendingActionRef.current = null;
      },
      onFinally: () => {
        setSubmitting(false);
      },
    });
  }, []);

  return {
    isPinEnabledForModule: isEnabledForModule,
    modalProps: {
      isOpen,
      onClose: closeModal,
      onConfirm: confirmPin,
      errorMessage,
      autoSubmit: operatorPinAutoSubmit,
      submitting,
    },
    openForAction,
    runWithOperatorPin,
  };
}
