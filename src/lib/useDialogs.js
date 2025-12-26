import { useState } from "react";

export function useConfirmDialog() {
  const [confirmState, setConfirmState] = useState(null);

  const requestConfirm = (options) =>
    new Promise((resolve) => {
      setConfirmState({ ...options, resolve });
    });

  const handleConfirm = () => {
    confirmState?.resolve(true);
    setConfirmState(null);
  };

  const handleCancel = () => {
    confirmState?.resolve(false);
    setConfirmState(null);
  };

  return { confirmState, requestConfirm, handleConfirm, handleCancel };
}

export function usePromptDialog() {
  const [promptState, setPromptState] = useState(null);
  const [promptValue, setPromptValue] = useState("");

  const requestPrompt = (options) =>
    new Promise((resolve) => {
      setPromptValue(options?.defaultValue || "");
      setPromptState({ ...options, resolve });
    });

  const handleConfirm = () => {
    promptState?.resolve(promptValue);
    setPromptState(null);
  };

  const handleCancel = () => {
    promptState?.resolve(null);
    setPromptState(null);
  };

  return {
    promptState,
    promptValue,
    setPromptValue,
    requestPrompt,
    handleConfirm,
    handleCancel,
  };
}
